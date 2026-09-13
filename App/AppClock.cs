using System;
using System.Globalization;

namespace KeRing.App
{
    /// <summary>
    /// 时间基准。打铃完全依赖"现在几点"，而教室里的机器**有些系统时间确实不准**
    /// （主板电池没电、时区设错、从来没同步过），所以本程序不直接用系统时间打铃，
    /// 而是按下面的优先级算出一个修正量再加上去：
    ///
    ///   1. **NTP 服务器** —— 最可信，误差毫秒级
    ///   2. **教务接口响应里的 HTTP Date 头** —— 白捡的（本来就要请求），但那台服务器实测慢 14 秒，
    ///      所以**只在偏差很大时才采用**，见 ServerTrustThreshold
    ///   3. **本机系统时间** —— 兜底，修正量为 0
    ///
    /// 只加修正量，**不改系统时钟**：改系统时间要管理员权限，而且第 2 个源本身也不完美，
    /// 拿它去改全机的钟会把别的程序一起带偏。
    ///
    /// 时区固定按**北京时间（UTC+8）**换算：打铃是全校统一的北京时间，
    /// 机器时区设错不应该让铃差 8 小时。时区设错会在日志和自检里报出来。
    /// </summary>
    internal static class AppClock
    {
        /// <summary>北京时间偏移。全校统一，写死不跟本机时区走。</summary>
        private static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);

        /// <summary>
        /// 教务接口的时间偏差超过这个值才采用它。理由：实测那台服务器**慢 14 秒**，
        /// 常态下"谁都不动"比"用一个慢 14 秒的钟去校准"更准；只有本机钟明显坏了
        /// （差一分钟以上）才退而求其次用它——那时候它至少比坏钟靠谱。
        /// </summary>
        private static readonly TimeSpan ServerTrustThreshold = TimeSpan.FromMinutes(1);

        private static readonly object Gate = new object();

        /// <summary>标准 UTC − 本机 UTC。</summary>
        private static TimeSpan _offset = TimeSpan.Zero;

        private const int RankNtp = 1;
        private const int RankServer = 2;
        private const int RankLocal = 3;

        private static int _rank = RankLocal;
        private static string _source = "本机系统时间";
        private static DateTime _measuredAtUtc = DateTime.MinValue;
        private static TimeSpan _lifetime = TimeSpan.MaxValue;

        private static TimeSpan? _serverSkew;
        private static TimeSpan? _ntpSkew;
        private static string _ntpServer = string.Empty;
        private static DateTime _lastNtpAttemptUtc = DateTime.MinValue;
        private static bool _lastNtpOk;

        /// <summary>校准后的 UTC。</summary>
        public static DateTime UtcNow
        {
            get { lock (Gate) { return DateTime.UtcNow + _offset; } }
        }

        /// <summary>
        /// 校准后的**北京时间**。全程序凡是要"现在几点"的地方都用它，别用 DateTime.Now。
        /// 返回值刻意标成 Unspecified：它可能跟本机时区不一致（机器时区设错时），
        /// 标明 Kind 反而误导。
        /// </summary>
        public static DateTime Now
        {
            get { return DateTime.SpecifyKind(UtcNow + BeijingOffset, DateTimeKind.Unspecified); }
        }

        /// <summary>当前采用的是哪个时间源，写日志和自检用。</summary>
        public static string Source
        {
            get { lock (Gate) { return _source; } }
        }

        /// <summary>当前修正量（标准 UTC − 本机 UTC）。</summary>
        public static TimeSpan Offset
        {
            get { lock (Gate) { return _offset; } }
        }

        public static bool IsCorrected
        {
            get { lock (Gate) { return _rank < RankLocal && _offset != TimeSpan.Zero; } }
        }

        /// <summary>最近一次量到的教务接口偏差（不代表已采用）。</summary>
        public static TimeSpan? ServerSkew
        {
            get { lock (Gate) { return _serverSkew; } }
        }

        public static string NtpServer
        {
            get { lock (Gate) { return _ntpServer; } }
        }

        public static TimeSpan? NtpSkew
        {
            get { lock (Gate) { return _ntpSkew; } }
        }

        /// <summary>本机时区是不是北京时间（UTC+8）。</summary>
        public static bool IsBeijingTimeZone
        {
            get
            {
                try { return TimeZoneInfo.Local.BaseUtcOffset == BeijingOffset; }
                catch { return false; }
            }
        }

        public static string TimeZoneDescription
        {
            get
            {
                try
                {
                    var zone = TimeZoneInfo.Local;
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}（UTC{1}{2:00}:{3:00}）",
                        zone.Id,
                        zone.BaseUtcOffset < TimeSpan.Zero ? "-" : "+",
                        Math.Abs(zone.BaseUtcOffset.Hours),
                        Math.Abs(zone.BaseUtcOffset.Minutes));
                }
                catch
                {
                    return "未知";
                }
            }
        }

        /// <summary>
        /// 按配置依次问一遍 NTP，第一个答上来的就采用。返回是否成功。
        /// **会阻塞**（每个服务器最多 timeoutSeconds 秒），只允许在后台线程里调。
        /// </summary>
        public static bool SyncNtp(string servers, int timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(servers)) { return false; }

            // 总时间也要有个上限：服务器不通时是静默丢包，只能靠超时结束，
            // 4 个服务器 × 2 秒就 8 秒了，太拖。到点就放弃，下次再说。
            var deadline = DateTime.UtcNow.AddSeconds(Math.Max(3, timeoutSeconds * 3));

            foreach (var item in servers.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var host = item.Trim();
                if (host.Length == 0) { continue; }
                if (DateTime.UtcNow > deadline)
                {
                    Logger.Warn("NTP 校时超时，本轮放弃");
                    break;
                }

                var offset = NtpClient.Query(host, timeoutSeconds);
                if (!offset.HasValue) { continue; }

                Adopt(offset.Value, RankNtp, "NTP " + host, TimeSpan.FromHours(6));

                lock (Gate)
                {
                    _ntpSkew = offset.Value;
                    _ntpServer = host;
                }

                Logger.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    "时间已校准到 NTP：{0}，本机时间比标准时间{1} {2} 秒",
                    host,
                    offset.Value < TimeSpan.Zero ? "快" : "慢",
                    Math.Abs(offset.Value.TotalSeconds).ToString("0.00")));
                return true;
            }

            Logger.Warn("所有 NTP 服务器都没应答，改用下一级时间源");
            return false;
        }

        /// <summary>
        /// 按需校时：成功过就半小时再来一次，失败过就两小时一次
        /// （别在不通的网络上每次刷新都白等几秒）。刷新课表时顺手调它。
        /// </summary>
        public static bool SyncNtpIfDue(string servers, int timeoutSeconds)
        {
            lock (Gate)
            {
                if (_lastNtpAttemptUtc != DateTime.MinValue)
                {
                    var wait = _lastNtpOk ? TimeSpan.FromMinutes(30) : TimeSpan.FromHours(2);
                    if (DateTime.UtcNow - _lastNtpAttemptUtc < wait) { return _lastNtpOk; }
                }

                _lastNtpAttemptUtc = DateTime.UtcNow;
            }

            var ok = SyncNtp(servers, timeoutSeconds);
            lock (Gate) { _lastNtpOk = ok; }
            return ok;
        }

        /// <summary>
        /// 教务接口报时间。优先级低于 NTP，而且**偏差不够大就不采用**——
        /// 理由见 ServerTrustThreshold。
        /// </summary>
        public static void ReportServerTime(DateTime serverUtc, DateTime localUtc)
        {
            var offset = serverUtc - localUtc;
            var adopted = false;

            lock (Gate)
            {
                _serverSkew = offset;

                if (!IsNtpFresh() && offset.Duration() > ServerTrustThreshold)
                {
                    _offset = offset;
                    _rank = RankServer;
                    _source = "教务接口 Date 头";
                    _measuredAtUtc = DateTime.UtcNow;
                    _lifetime = TimeSpan.FromMinutes(90);
                    adopted = true;
                }
            }

            if (adopted)
            {
                Logger.Warn(string.Format(
                    CultureInfo.InvariantCulture,
                    "本机时间与教务服务器差了 {0} 秒，已按服务器校准（注意：那台服务器本身也不够准）",
                    offset.TotalSeconds.ToString("0.0")));
            }
        }

        /// <summary>时区检查。设错不影响打铃（显示一律按北京时间），但要留个记录。</summary>
        public static bool CheckTimeZone()
        {
            if (IsBeijingTimeZone) { return true; }

            Logger.Warn("本机时区不是北京时间（当前 " + TimeZoneDescription +
                        "），程序仍按北京时间打铃；建议把系统时区改成 UTC+08:00");
            return false;
        }

        /// <summary>把当前采用的时间源写进日志，出问题时好对照。</summary>
        public static void LogCurrentSource()
        {
            string source;
            TimeSpan offset;
            lock (Gate)
            {
                source = _source;
                offset = _offset;
            }

            Logger.Info(string.Format(
                CultureInfo.InvariantCulture,
                "时间基准：{0}；修正 {1} 秒；当前北京时间 {2:yyyy-MM-dd HH:mm:ss}",
                source,
                offset.TotalSeconds.ToString("+0.00;-0.00;0.00"),
                Now));
        }

        private static bool IsNtpFresh()
        {
            return _rank == RankNtp && _measuredAtUtc != DateTime.MinValue &&
                   (DateTime.UtcNow - _measuredAtUtc) <= _lifetime;
        }

        private static void Adopt(TimeSpan offset, int rank, string source, TimeSpan lifetime)
        {
            lock (Gate)
            {
                _offset = offset;
                _rank = rank;
                _source = source;
                _measuredAtUtc = DateTime.UtcNow;
                _lifetime = lifetime;
            }
        }
    }
}

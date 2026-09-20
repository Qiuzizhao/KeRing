using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace KeRing.App
{
    /// <summary>
    /// 配置文件（kering.config.json），放在数据目录（见 AppPaths），直接改文本即可。
    /// </summary>
    internal sealed class AppConfig
    {
        // ---- 数据源 ----
        /// <summary>演示用：本地课表文件（相对路径按数据目录解析）。只有没配接口地址时才用它。</summary>
        public string ScheduleFilePath { get; set; } = "schedule.json";

        /// <summary>
        /// 正式课表接口地址。**留空则退回本地演示文件**（见 ScheduleSourceFactory）。
        /// 默认值就是学校的全校课表接口，教室机器配好就能直接用。
        /// </summary>
        public string ScheduleApiUrl { get; set; } = "http://10.40.1.1/php/dktk/dktkView.php";

        /// <summary>
        /// 接口身份标签：cookie usernameEncoded 的值。学校接口只校验它非空、不校验身份
        /// （见 docs/课表接口采集方案.md 3.4），所以这里既不是账号也不是密码，
        /// 用一个能标识来源的名字即可，方便在学校服务器日志里认出来。
        /// </summary>
        public string ScheduleCookieUser { get; set; } = "KeRing";

        /// <summary>接口请求超时（秒）。</summary>
        public int ScheduleTimeoutSeconds { get; set; } = 10;

        /// <summary>
        /// 时间校准用的 NTP 服务器，逗号分隔，按顺序试，第一个答上来的就用。
        /// 教室机器如果连不上公网 NTP，**填学校内网的 NTP 或域控地址**；
        /// 留空表示不查 NTP，退回用教务接口的时间。
        /// </summary>
        public string NtpServers { get; set; } = "ntp.aliyun.com,cn.pool.ntp.org,ntp.ntsc.ac.cn";

        /// <summary>每个 NTP 服务器的等待秒数。NTP 走 UDP，不能用 HTTP 代理，超时要短。</summary>
        public int NtpTimeoutSeconds { get; set; } = 2;

        /// <summary>自动刷新间隔（分钟）。</summary>
        public int RefreshIntervalMinutes { get; set; } = 30;

        /// <summary>上午上到第几节，在第几节后面插一条分隔色带（上下午分割）。填 0 表示不分割。</summary>
        public int MorningSplitAfterPeriod { get; set; } = 4;

        /// <summary>作息方案：高年级 / 低年级。默认高年级。时刻以方案表为准，见 GradeSchemes。</summary>
        public string GradeScheme { get; set; } = GradeSchemes.Senior;

        /// <summary>
        /// 这台机器使用哪个班级的课表（数据源里的班级 Id）。
        /// 空 = 还没选过，首次运行时会弹"选择班级"；选过之后一直记着，重开不用再选。
        /// </summary>
        public string SelectedClassId { get; set; } = string.Empty;

        // ---- 打铃规则 ----
        /// <summary>
        /// 提前提醒的档位（分钟）：上课前第 N 分钟响一次。**降序、去重、0~60、最多 4 档**，
        /// 空列表 = 不打铃（使用者故意全关掉）。
        /// 例：[7, 5] = 上课前 7 分钟响一次、5 分钟再响一次；0 = 正点（等于上课铃）。
        /// 界面上是"快捷开关"（档位见 RemindAheadPresets），但配置里允许任意值——
        /// 不在预设里的值，设置对话框也会把它显示成一个开关，不会一打开设置就被悄悄改掉。
        /// **null = 配置里根本没有这个字段（老配置）**，由 Normalize 用旧字段补上。
        /// </summary>
        public List<int> RemindAheadList { get; set; }

        /// <summary>
        /// 【旧字段，只为兼容】单值版的提前分钟数。老配置里只有它，`Normalize` 会把它搬进
        /// `RemindAheadList`；反过来说，保存时它会被同步成"列表里最大的那一档"，
        /// 好让**老版本的程序**读这份配置仍然正常（回滚或新旧混用时不至于莫名不响铃）。
        /// </summary>
        public int RemindAheadMinutes { get; set; } = 7;

        /// <summary>设置对话框里那排"快捷开关"的档位（分钟）。</summary>
        public static readonly int[] RemindAheadPresets = { 10, 7, 5, 3, 0 };

        /// <summary>
        /// 没设过时默认点亮哪几档（使用方 2026-09-18 定的）：**班级版默认"7 分 + 3 分"**
        /// （上课前 7 分钟提醒一次、3 分钟再提醒一次），个人版只默认 7 分。
        /// </summary>
        public static readonly int[] RemindAheadDefaults = { 7, 3 };

        /// <summary>
        /// 老配置里"单值那个字段"的老默认值。老配置没有列表字段时：等于它（说明没人动过提前量）
        /// 就上新的默认档位；不等于它（有人改过，比如设成 5 分钟）就只留他改的那一档，不自作主张加档。
        /// </summary>
        private const int LegacyDefaultAheadMinutes = 7;

        /// <summary>最多允许几档提醒（档位多了会连着响个不停，最多 4 个）。</summary>
        public const int MaxRemindAheadSlots = 4;

        /// <summary>播报时临时提升到的系统音量（百分比），播完恢复原值。</summary>
        public int AnnounceVolumePercent { get; set; } = 100;

        /// <summary>
        /// 一条播报念几遍。**每遍都是"先响一次提示音，再念这句话"**，两遍之间停一下。
        /// 默认 3 遍：学生/老师漏听一遍还有下一遍（跟个人版一致，2026-09-16 加）。
        /// </summary>
        public int AnnounceRepeatCount { get; set; } = 3;

        /// <summary>播报文案前缀。</summary>
        public string AnnouncePrefix { get; set; } = "下节课，";

        /// <summary>播报文案后缀。</summary>
        public string AnnounceSuffix { get; set; } = "，请同学们做好课前准备";

        // ---- 悬浮窗（见 docs/开发交接.md 7.12）----
        /// <summary>要不要显示悬浮窗。**默认开**（使用方 2026-09-16 定的）。</summary>
        public bool FloatingEnabled { get; set; } = true;

        /// <summary>悬浮窗上次的位置。int.MinValue = 还没拖过，首次摆到屏幕右侧、垂直居中。</summary>
        public int FloatingLeft { get; set; } = int.MinValue;
        public int FloatingTop { get; set; } = int.MinValue;

        /// <summary>悬浮窗不透明度（40~100，默认 92）：既能看见，又不挡死底下的东西。</summary>
        public int FloatingOpacity { get; set; } = 92;

        /// <summary>鼠标移到悬浮窗上要不要自动弹出全周预览（默认开）。</summary>
        public bool FloatingShowWeekOnHover { get; set; } = true;

        /// <summary>
        /// 悬浮窗是否置顶。**默认不置顶**（＝沉在下面，会被别的窗口盖住；使用方 2026-09-16 定的）——
        /// 小窗表头上那个**图钉**按钮切的就是它（见 docs/开发交接.md 7.12）。
        /// </summary>
        public bool FloatingTopMost { get; set; }

        // ---- 运行方式 ----
        /// <summary>开机自启，默认开启（安装后即生效）。</summary>
        public bool AutoStart { get; set; } = true;

        /// <summary>
        /// **挂了自动拉起来**（看门狗，见 App/Watchdog.cs），默认开。
        /// 教室机器上这条比什么都重要——铃悄悄不响了没人知道才是最糟的。
        /// </summary>
        public bool WatchdogEnabled { get; set; } = true;

        /// <summary>
        /// 看门狗计划任务登记的是哪个 exe。空 = 没登记成功（被系统策略挡住）或已关闭；
        /// 程序换目录之后它会自动重新登记（跟开机自启的处理一样）。
        /// </summary>
        public string WatchdogTaskPath { get; set; } = string.Empty;

        /// <summary>上一次登记计划任务失败的原因（空 = 没失败过）。被系统策略挡住时这里会写明，自检里能看到。</summary>
        public string WatchdogLastError { get; set; } = string.Empty;

        public bool StartMinimized { get; set; }
        public bool CloseToTray { get; set; } = true;

        [JsonIgnore]
        public string ConfigPath { get; set; }

        [JsonIgnore]
        public string LastLoadError { get; set; }

        public static AppConfig Load()
        {
            var path = AppPaths.ConfigFile;
            try
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var loaded = JsonConvert.DeserializeObject<AppConfig>(json);
                    if (loaded != null)
                    {
                        loaded.ConfigPath = path;
                        loaded.Normalize();
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("读取配置失败，改用默认配置", ex);
            }

            var fresh = new AppConfig { ConfigPath = path };
            fresh.Normalize();
            fresh.Save();
            return fresh;
        }

        public void Save()
        {
            try
            {
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(ConfigPath ?? AppPaths.ConfigFile, json);
            }
            catch (Exception ex)
            {
                Logger.Error("保存配置失败", ex);
            }
        }

        private void Normalize()
        {
            if (string.IsNullOrWhiteSpace(ScheduleFilePath)) { ScheduleFilePath = "schedule.json"; }
            RemindAheadMinutes = Clamp(RemindAheadMinutes, 0, 60);
            RemindAheadList = NormalizeAheadList(RemindAheadList, RemindAheadMinutes);
            // 老字段跟着列表走：让旧版本的程序读这份配置时还有合理的值（列表被全关掉时保持原值）
            if (RemindAheadList.Count > 0) { RemindAheadMinutes = RemindAheadList[0]; }
            AnnounceVolumePercent = Clamp(AnnounceVolumePercent, 0, 100);
            AnnounceRepeatCount = Clamp(AnnounceRepeatCount, 1, 10);
            RefreshIntervalMinutes = Clamp(RefreshIntervalMinutes, 1, 24 * 60);
            MorningSplitAfterPeriod = Clamp(MorningSplitAfterPeriod, 0, 20);
            ScheduleTimeoutSeconds = Clamp(ScheduleTimeoutSeconds, 1, 60);
            NtpTimeoutSeconds = Clamp(NtpTimeoutSeconds, 1, 30);
            FloatingOpacity = Clamp(FloatingOpacity, 40, 100);
            GradeScheme = GradeSchemes.Normalize(GradeScheme);
            if (ScheduleApiUrl == null) { ScheduleApiUrl = string.Empty; }
            if (string.IsNullOrWhiteSpace(ScheduleCookieUser)) { ScheduleCookieUser = "KeRing"; }
            if (NtpServers == null) { NtpServers = string.Empty; }
            if (SelectedClassId == null) { SelectedClassId = string.Empty; }
            if (WatchdogTaskPath == null) { WatchdogTaskPath = string.Empty; }
            if (WatchdogLastError == null) { WatchdogLastError = string.Empty; }
            if (AnnouncePrefix == null) { AnnouncePrefix = string.Empty; }
            if (AnnounceSuffix == null) { AnnounceSuffix = string.Empty; }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) { return min; }
            if (value > max) { return max; }
            return value;
        }

        /// <summary>
        /// 整理提前提醒的档位：去掉越界值、去重、**大的在前**（先"提前 10 分钟"再"提前 5 分钟"），
        /// 超过 MaxRemindAheadSlots 档就把最靠后的（最接近上课的）丢掉。
        /// `raw == null` 表示配置里没有这个字段（老配置）→ 用旧字段的单值补一档；
        /// `raw` 是**空列表**表示使用者故意把提醒全关了 → 保持空，不补。
        /// </summary>
        private static List<int> NormalizeAheadList(List<int> raw, int legacyMinutes)
        {
            var result = new List<int>();

            if (raw == null)
            {
                // 老配置（只有单值字段）：值正好是老默认 7 分钟 = 没人动过 → 用新的默认档位；
                // 被人改过（例如 5 分钟）→ 只留他那一档，不自作主张多响一次
                var legacy = Clamp(legacyMinutes, 0, 60);
                var values = legacy == LegacyDefaultAheadMinutes ? RemindAheadDefaults : new[] { legacy };
                foreach (var value in values)
                {
                    if (!result.Contains(value)) { result.Add(value); }
                }
            }
            else
            {
                foreach (var value in raw)
                {
                    var minutes = Clamp(value, 0, 60);
                    if (!result.Contains(minutes)) { result.Add(minutes); }
                }
            }

            result.Sort((a, b) => b.CompareTo(a));
            while (result.Count > MaxRemindAheadSlots) { result.RemoveAt(result.Count - 1); }
            return result;
        }
    }
}

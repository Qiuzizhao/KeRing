using System;
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
        /// <summary>提前几分钟提醒。当前约定：上课前 7 分钟。</summary>
        public int RemindAheadMinutes { get; set; } = 7;

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

        // ---- 运行方式 ----
        /// <summary>开机自启，默认开启（安装后即生效）。</summary>
        public bool AutoStart { get; set; } = true;
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
            AnnounceVolumePercent = Clamp(AnnounceVolumePercent, 0, 100);
            AnnounceRepeatCount = Clamp(AnnounceRepeatCount, 1, 10);
            RefreshIntervalMinutes = Clamp(RefreshIntervalMinutes, 1, 24 * 60);
            MorningSplitAfterPeriod = Clamp(MorningSplitAfterPeriod, 0, 20);
            ScheduleTimeoutSeconds = Clamp(ScheduleTimeoutSeconds, 1, 60);
            NtpTimeoutSeconds = Clamp(NtpTimeoutSeconds, 1, 30);
            GradeScheme = GradeSchemes.Normalize(GradeScheme);
            if (ScheduleApiUrl == null) { ScheduleApiUrl = string.Empty; }
            if (string.IsNullOrWhiteSpace(ScheduleCookieUser)) { ScheduleCookieUser = "KeRing"; }
            if (NtpServers == null) { NtpServers = string.Empty; }
            if (SelectedClassId == null) { SelectedClassId = string.Empty; }
            if (AnnouncePrefix == null) { AnnouncePrefix = string.Empty; }
            if (AnnounceSuffix == null) { AnnounceSuffix = string.Empty; }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) { return min; }
            if (value > max) { return max; }
            return value;
        }
    }
}

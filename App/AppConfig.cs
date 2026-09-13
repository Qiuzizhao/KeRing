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
        /// <summary>演示用：本地课表文件（相对路径按数据目录解析）。</summary>
        public string ScheduleFilePath { get; set; } = "schedule.json";

        /// <summary>预留：正式接入时改成接口地址，由解析模块消费。</summary>
        public string ScheduleApiUrl { get; set; } = string.Empty;

        /// <summary>自动刷新间隔（分钟）。</summary>
        public int RefreshIntervalMinutes { get; set; } = 30;

        /// <summary>上午上到第几节，在第几节后面插一条分隔色带（上下午分割）。填 0 表示不分割。</summary>
        public int MorningSplitAfterPeriod { get; set; } = 4;

        /// <summary>作息方案：高年级 / 低年级。默认高年级。时刻以方案表为准，见 GradeSchemes。</summary>
        public string GradeScheme { get; set; } = GradeSchemes.Senior;

        // ---- 打铃规则 ----
        /// <summary>提前几分钟提醒。当前约定：上课前 7 分钟。</summary>
        public int RemindAheadMinutes { get; set; } = 7;

        /// <summary>播报时临时提升到的系统音量（百分比），播完恢复原值。</summary>
        public int AnnounceVolumePercent { get; set; } = 100;

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
            RefreshIntervalMinutes = Clamp(RefreshIntervalMinutes, 1, 24 * 60);
            MorningSplitAfterPeriod = Clamp(MorningSplitAfterPeriod, 0, 20);
            GradeScheme = GradeSchemes.Normalize(GradeScheme);
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

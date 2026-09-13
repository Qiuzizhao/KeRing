namespace KeRing.App.Schedule
{
    /// <summary>
    /// 数据源选择。规则只有一条：**配置了接口地址就走学校接口，否则退回本地演示文件。**
    ///
    /// 这样教室里的机器配好地址就走正式数据，开发机上不配地址也能照样跑演示数据；
    /// 换数据源只改配置，不用改代码（开发交接.md 第五节第 2 条）。
    /// </summary>
    internal static class ScheduleSourceFactory
    {
        public static IScheduleSource Create(AppConfig config)
        {
            if (config != null && !string.IsNullOrWhiteSpace(config.ScheduleApiUrl))
            {
                return new HttpScheduleSource(
                    config.ScheduleApiUrl,
                    config.ScheduleCookieUser,
                    config.ScheduleTimeoutSeconds);
            }

            return new LocalFileScheduleSource(config == null ? "schedule.json" : config.ScheduleFilePath);
        }
    }
}

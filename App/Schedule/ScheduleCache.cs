using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace KeRing.App.Schedule
{
    /// <summary>
    /// 最后一次**成功取到**的课表，落一份盘。
    ///
    /// 为什么需要：课表原来是本地文件，"读不到"基本等于程序坏了；现在课表来自网络，
    /// 机器开机时交换机可能还没起来、网线可能正在被人拔，这时**取不到数据就一个铃都不响**。
    /// 所以留一份最近的，开机先拿它顶上，接口通了再换新的。
    ///
    /// 注意它跟 `schedule.json` 不是一回事：那个是程序自己造的演示课表（9 个假班），
    /// 这个是真实抓下来的全校课表。
    /// </summary>
    internal static class ScheduleCache
    {
        private const string FileName = "schedule-cache.json";

        public static string FilePath
        {
            get { return Path.Combine(AppPaths.DataDirectory, FileName); }
        }

        /// <summary>存。失败只记日志——缓存写不进去不能影响打铃。</summary>
        public static void Save(SchoolSchedule school, int week)
        {
            if (school == null) { return; }

            try
            {
                AppPaths.EnsureDirectories();
                var payload = new CacheFile
                {
                    SavedAt = AppClock.Now,
                    Week = week,
                    Schedule = school,
                };

                File.WriteAllText(
                    FilePath,
                    JsonConvert.SerializeObject(payload, Formatting.Indented),
                    new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Logger.Warn("保存课表缓存失败：" + ex.Message);
            }
        }

        /// <summary>读。没有、解析不了、内容为空都返回 null（坏文件不删，留着给人看）。</summary>
        public static SchoolSchedule Load(out DateTime savedAt, out int week)
        {
            savedAt = DateTime.MinValue;
            week = 0;

            try
            {
                if (!File.Exists(FilePath)) { return null; }

                var payload = JsonConvert.DeserializeObject<CacheFile>(
                    File.ReadAllText(FilePath, Encoding.UTF8));

                if (payload == null || payload.Schedule == null ||
                    payload.Schedule.Classes == null || payload.Schedule.Classes.Count == 0)
                {
                    return null;
                }

                savedAt = payload.SavedAt;
                week = payload.Week;
                return payload.Schedule;
            }
            catch (Exception ex)
            {
                Logger.Warn("读取课表缓存失败：" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// 只问"上次成功时是第几周、什么时候存的"，不解析整份课表。
        /// 给学校接口没给出周次时的兜底用（见 HttpScheduleSource.FallbackWeek）。
        /// </summary>
        public static bool TryGetWeek(out int week, out DateTime savedAt)
        {
            week = 0;
            savedAt = DateTime.MinValue;

            try
            {
                if (!File.Exists(FilePath)) { return false; }

                var header = JsonConvert.DeserializeObject<CacheFile>(
                    File.ReadAllText(FilePath, Encoding.UTF8));
                if (header == null) { return false; }

                week = header.Week;
                savedAt = header.SavedAt;
                return week > 0;
            }
            catch (Exception ex)
            {
                Logger.Warn("读取课表缓存里的周次失败：" + ex.Message);
                return false;
            }
        }

        private sealed class CacheFile
        {
            /// <summary>存这份缓存的时刻（校准后的北京时间）。</summary>
            public DateTime SavedAt { get; set; }

            /// <summary>当时是第几周，日志里用。</summary>
            public int Week { get; set; }

            public SchoolSchedule Schedule { get; set; }
        }
    }
}

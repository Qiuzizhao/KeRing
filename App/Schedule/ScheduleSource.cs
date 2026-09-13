using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace KeRing.App.Schedule
{
    internal sealed class ScheduleLoadResult
    {
        public bool Success;
        public string Message;
        public SchoolSchedule Schedule;
    }

    /// <summary>
    /// 课表来源。目前演示用本地文件；正式版按使用方给的接口规则再实现一个
    /// HttpScheduleSource（取数 + 解析都在里面），其余代码不用动。
    /// </summary>
    internal interface IScheduleSource
    {
        string Description { get; }
        ScheduleLoadResult Load();
    }

    internal sealed class LocalFileScheduleSource : IScheduleSource
    {
        private readonly string _path;

        public LocalFileScheduleSource(string path)
        {
            _path = path;
        }

        public string Description
        {
            get { return "本地文件 " + _path; }
        }

        public ScheduleLoadResult Load()
        {
            var result = new ScheduleLoadResult();
            try
            {
                var fullPath = AppPaths.Resolve(_path);
                if (!File.Exists(fullPath))
                {
                    SampleSchedule.WriteTo(fullPath);
                    Logger.Info("未找到课表文件，已生成演示数据：" + fullPath);
                }

                var json = File.ReadAllText(fullPath, Encoding.UTF8);
                var dto = JsonConvert.DeserializeObject<ScheduleFileDto>(json);
                if (dto == null)
                {
                    result.Success = false;
                    result.Message = "课表内容无法解析：" + fullPath;
                    return result;
                }

                var school = new SchoolSchedule
                {
                    GeneratedAt = dto.GeneratedAt,
                    Periods = dto.Periods ?? new List<Period>(),
                };

                if (dto.Classes != null && dto.Classes.Count > 0)
                {
                    foreach (var item in dto.Classes)
                    {
                        if (item == null) { continue; }
                        item.Entries = item.Entries ?? new List<CourseEntry>();
                        school.Classes.Add(item);
                    }
                }
                else if (dto.Entries != null)
                {
                    // 兼容旧格式：整份文件就是一个班，包成单班
                    school.Classes.Add(new SchoolClass
                    {
                        Id = "default",
                        Name = "默认班级",
                        Entries = dto.Entries,
                    });
                }

                if (school.Classes.Count == 0)
                {
                    result.Success = false;
                    result.Message = "课表里没有任何班级：" + fullPath;
                    return result;
                }

                result.Schedule = school;
                result.Success = true;
                result.Message = string.Format(
                    "已加载 {0} 个班级（{1:MM-dd HH:mm}）",
                    school.Classes.Count,
                    DateTime.Now);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = "读取课表失败：" + ex.Message;
                Logger.Error("读取课表失败：" + _path, ex);
            }

            return result;
        }
    }

    internal sealed class ScheduleFileDto
    {
        [JsonProperty("generated_at")]
        public DateTime? GeneratedAt { get; set; }

        [JsonProperty("periods")]
        public List<Period> Periods { get; set; }

        [JsonProperty("classes")]
        public List<SchoolClass> Classes { get; set; }

        /// <summary>旧格式：单班课表。保留是为了兼容已经生成过的 schedule.json。</summary>
        [JsonProperty("entries")]
        public List<CourseEntry> Entries { get; set; }
    }
}

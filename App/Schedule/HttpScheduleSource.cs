using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json;

namespace KeRing.App.Schedule
{
    /// <summary>
    /// 学校教务接口取全校课表（新版 PHP dktk）。
    ///
    /// 抓取路子跟参考项目 WorkSchedule 一致：POST dktkView.php 一次拿回全校所有班级。
    /// 但按我们自己的要求做了三处不同的处理（见 docs/课表接口采集方案.md 第四节）：
    ///   1. 只要课程名，**不解析教师**（格子里教师那半截直接丢）
    ///   2. **保留完整 7 天**，不把周末截掉
    ///   3. **不采晨午管理等附加时段**（那是 dktkTools.php，另一条接口，我们不调）
    ///
    /// 注意：这个接口的"身份校验"只是 cookie usernameEncoded 非空，不是真登录，
    /// 所以这里不保存也不使用任何账号密码——教室里那台机器不该存教师凭据。
    /// </summary>
    internal sealed class HttpScheduleSource : IScheduleSource
    {
        /// <summary>最多 7 天。接口按 knPerWeek / knPerDay 推天数，我们只取到周日为止。</summary>
        private const int MaxDays = 7;

        /// <summary>
        /// 科目单字 → 课程名。学校把这套代号写死在它的表格里，科目代码都是单字，
        /// 取自参考项目的 _DKTK_SUBJECT_MAP。
        /// </summary>
        private static readonly Dictionary<string, string> SubjectMap =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "语", "语文" }, { "数", "数学" }, { "英", "英语" }, { "口", "口语" },
                { "健", "健康" }, { "体", "体育" }, { "阅", "阅读" }, { "道", "道法" },
                { "科", "科学" }, { "探", "探索" }, { "美", "美术" }, { "音", "音乐" },
                { "综", "综合" }, { "劳", "劳动" }, { "电", "电脑" }, { "书", "书法" },
                { "班", "班会" },
            };

        private readonly string _apiUrl;
        private readonly string _cookieUser;
        private readonly int _timeoutSeconds;

        public HttpScheduleSource(string apiUrl, string cookieUser, int timeoutSeconds)
        {
            _apiUrl = (apiUrl ?? string.Empty).Trim();
            _cookieUser = (cookieUser ?? string.Empty).Trim();
            _timeoutSeconds = timeoutSeconds < 1 ? 10 : timeoutSeconds;
        }

        public string Description
        {
            get { return "学校接口 " + _apiUrl; }
        }

        public ScheduleLoadResult Load()
        {
            var result = new ScheduleLoadResult();

            if (string.IsNullOrWhiteSpace(_apiUrl))
            {
                result.Message = "没有配置课表接口地址";
                return result;
            }

            if (string.IsNullOrWhiteSpace(_cookieUser))
            {
                result.Message = "没有配置接口用户名（cookie usernameEncoded）";
                return result;
            }

            try
            {
                // 第一步：用 kWeek=0 探测当前周。
                // 不能图省事直接拿 kWeek=0 那份课表——接口只在带真实周次时才返回该周的
                // 调课记录（实测 kWeek=0 的 dktkInfoAoa 是空的，课表也就不是那一周该有的样子）。
                var probe = Request("0");
                if (probe == null || probe.Result != 1)
                {
                    result.Message = "接口探测失败：" + DescribeFailure(probe);
                    Logger.Warn("课表接口探测失败：" + result.Message);
                    return result;
                }

                // 周次以学校接口的 curKWeek 为准；它不给就按"上次成功时的周次"往后推（见 FallbackWeek）
                var week = probe.CurrentWeek;
                if (week <= 0)
                {
                    week = FallbackWeek();
                    if (week > 0)
                    {
                        Logger.Warn("学校接口没给出当前周次，按缓存里的周次推算：第 " + week + " 周");
                    }
                    else
                    {
                        // 连缓存都没有（从没成功过），只能瞎猜第 1 周——课表很可能是错的
                        week = 1;
                        Logger.Warn("学校接口没给出当前周次，也没有可用缓存，只能按第 1 周处理（课表可能是错的）");
                    }
                }

                // 第二步：取那一周
                var data = Request(week.ToString(CultureInfo.InvariantCulture));
                if (data == null || data.Result != 1)
                {
                    result.Message = "接口返回失败：" + DescribeFailure(data);
                    Logger.Warn("课表接口返回失败：" + result.Message);
                    return result;
                }

                var school = Parse(data, week);
                if (school == null)
                {
                    result.Message = "接口返回的课表里没有任何班级";
                    Logger.Warn(result.Message);
                    return result;
                }

                // 整所学校一条课都没有 = 接口抽风，不能拿它去覆盖现有的课表
                // （假期按约定机器是关着的，所以工作日全空基本只有坏数据这一种可能）
                if (!HasAnyCourse(school))
                {
                    result.Message = "接口返回的课表是空的（整所学校一条课都没有），已忽略这一次";
                    Logger.Warn(result.Message);
                    return result;
                }

                result.Schedule = school;
                result.Week = week;
                result.Success = true;
                result.Message = string.Format(
                    "已加载 {0} 个班（第 {1} 周，{2} 天 × {3} 节，{4:MM-dd HH:mm}）",
                    school.Classes.Count,
                    week,
                    data.DaysPerWeek,
                    data.PeriodsPerDay,
                    AppClock.Now);
            }
            catch (Exception ex)
            {
                result.Message = "读取课表失败：" + ex.Message;
                Logger.Error("读取课表接口失败：" + _apiUrl, ex);
            }

            return result;
        }

        /// <summary>把接口返回的 JSON 组装成我们自己的模型。失败返回 null。</summary>
        private static SchoolSchedule Parse(DktkResponse data, int week)
        {
            if (data.Classes == null || data.Classes.Count == 0) { return null; }

            var periodsPerDay = data.PeriodsPerDay > 0 ? data.PeriodsPerDay : 6;
            var slotsPerWeek = data.SlotsPerWeek > 0 ? data.SlotsPerWeek : periodsPerDay * MaxDays;
            var dayCount = Math.Max(1, Math.Min(MaxDays, slotsPerWeek / periodsPerDay));

            // 先把每个班摊平成"按天铺开"的一维课程名数组，再叠加调课，
            // 最后才转成 CourseEntry——调课是整格搬移，必须在网格上做。
            var grids = new List<Cell[]>(data.Classes.Count);
            var names = new List<string>(data.Classes.Count);
            var unknownCells = 0;
            var unknownSamples = new List<string>();

            foreach (var item in data.Classes)
            {
                if (item == null) { continue; }

                var name = (item.Name ?? string.Empty).Trim();
                if (name.Length == 0) { continue; }

                var values = item.Values ?? new List<object>();
                var grid = new Cell[dayCount * periodsPerDay];
                for (var i = 0; i < grid.Length; i++)
                {
                    var text = i < values.Count ? ToCourseName(values[i]) : string.Empty;
                    if (text.Length > 0 && !IsKnownCourse(text))
                    {
                        unknownCells++;
                        if (unknownSamples.Count < 8 && !unknownSamples.Contains(text)) { unknownSamples.Add(text); }
                    }

                    grid[i] = new Cell { Course = text };
                }

                names.Add(name);
                grids.Add(grid);
            }

            if (names.Count == 0) { return null; }

            ApplyAdjustments(grids, data.Adjustments, periodsPerDay, dayCount, week);

            if (unknownCells > 0)
            {
                // 认不出来的格子原样保留（宁可界面上显示怪字符，也不要静默丢掉一节课），
                // 但把**具体是哪些字**记下来——学校一旦新增科目代号，照日志往 SubjectMap 补一条就行
                Logger.Warn(string.Format(
                    "课表里有 {0} 个格子认不出科目代号（{1}），已原样保留",
                    unknownCells,
                    string.Join("、", unknownSamples.ToArray())));
            }

            var school = new SchoolSchedule
            {
                GeneratedAt = AppClock.Now,
                // 时刻不从这里来——课表数据只提供"星期几、第几节、上什么课"，
                // 几点上由作息方案表决定（开发交接.md 第五节第 1 条），所以这里不填 Periods。
                Periods = new List<Period>(),
            };

            for (var c = 0; c < names.Count; c++)
            {
                var grid = grids[c];
                var entries = new List<CourseEntry>();
                for (var day = 0; day < dayCount; day++)
                {
                    for (var sec = 0; sec < periodsPerDay; sec++)
                    {
                        var cell = grid[day * periodsPerDay + sec];
                        if (string.IsNullOrWhiteSpace(cell.Course)) { continue; }   // 空堂不生成打铃点

                        entries.Add(new CourseEntry
                        {
                            Weekday = day + 1,      // 1 = 周一 … 7 = 周日
                            Period = sec + 1,
                            Course = cell.Course,
                            Adjusted = cell.Adjusted,
                        });
                    }
                }

                school.Classes.Add(new SchoolClass
                {
                    // 接口没有稳定 id，用班级名当 id。代价是学校改班级命名会导致每台机器要重选，
                    // 这一点写在方案文档的待确认清单里。
                    Id = names[c],
                    Name = names[c],
                    Entries = entries,
                });
            }

            return school;
        }

        /// <summary>
        /// 叠加调课（keType = 2）：把两个格子的内容互换。
        ///
        /// 只处理调课，其余三种**刻意跳过**：
        ///   1 代课、3 帮课、7 预代课都只是换老师，课还是那节课，课程名不变；
        ///   参考项目会把整格改写成"代某某"，那是为了显示代课老师，对我们没意义。
        /// 调课则会真的把课挪到别的节次，直接影响"这一节有没有课、该不该打铃"，所以要叠加。
        /// </summary>
        private static void ApplyAdjustments(
            List<Cell[]> grids,
            List<List<object>> records,
            int periodsPerDay,
            int dayCount,
            int week)
        {
            if (records == null || records.Count == 0) { return; }

            // 第 0 行是空表头，数据从第 1 行开始
            for (var r = 1; r < records.Count; r++)
            {
                var record = records[r];
                if (record == null || record.Count < 8) { continue; }

                if (ToInt(record[3]) != 2) { continue; }        // 只看调课

                var classIndex = ToInt(record[1]) - 1;
                if (classIndex < 0 || classIndex >= grids.Count) { continue; }

                var grid = grids[classIndex];
                var src = ToInt(record[2]) - 1;
                var dest = ToInt(record[6]) - 1;
                if (src < 0 || src >= grid.Length || src >= dayCount * periodsPerDay) { continue; }

                var weekOfSrc = ToInt(record[9]);
                var weekOfDest = record.Count > 5 ? ToInt(record[5]) : 0;

                if (weekOfSrc == weekOfDest)
                {
                    // 同周调课：两个格子内容互换，两格都标成"调过"
                    if (dest < 0 || dest >= grid.Length || dest == src) { continue; }

                    var swap = grid[src];
                    grid[src] = grid[dest];
                    grid[dest] = swap;
                    grid[src].Adjusted = true;
                    grid[dest].Adjusted = true;
                }
                else if (weekOfSrc == week)
                {
                    // 跨周调课：只影响本周所在的那一侧，源格换成记录里带来的课程
                    var value = record.Count > 8 ? ToCourseName(record[8]) : string.Empty;
                    if (value.Length > 0)
                    {
                        grid[src].Course = value;
                        grid[src].Adjusted = true;
                    }
                }
                else if (weekOfDest == week && dest >= 0 && dest < grid.Length)
                {
                    var value = record.Count > 11 ? ToCourseName(record[11]) : string.Empty;
                    if (value.Length > 0)
                    {
                        grid[dest].Course = value;
                        grid[dest].Adjusted = true;
                    }
                }
            }
        }

        /// <summary>网格里的一格。课程名之外还要记住"是不是调课调过来的"，界面要标蓝。</summary>
        private sealed class Cell
        {
            /// <summary>课程名（已经查过科目表，不含教师）。空 = 空堂。</summary>
            public string Course = string.Empty;

            /// <summary>被调课动过。</summary>
            public bool Adjusted;
        }

        /// <summary>
        /// 把一格的值转成课程名。
        /// 格子的格式是"科目单字 + 教师简名"两个字，例如 语菓 = 语文 + 袁艺菓。
        /// 我们只取首字查表得到课程，教师那半截直接丢掉。
        /// </summary>
        private static string ToCourseName(object raw)
        {
            var text = raw == null
                ? string.Empty
                : (Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty).Trim();
            if (text.Length == 0) { return string.Empty; }

            string course;
            if (SubjectMap.TryGetValue(text.Substring(0, 1), out course)) { return course; }

            return text;
        }

        private static bool IsKnownCourse(string course)
        {
            foreach (var value in SubjectMap.Values)
            {
                if (string.Equals(value, course, StringComparison.Ordinal)) { return true; }
            }

            return false;
        }

        /// <summary>整份课表里有没有任何一条课程。</summary>
        private static bool HasAnyCourse(SchoolSchedule school)
        {
            foreach (var item in school.Classes)
            {
                if (item.Entries != null && item.Entries.Count > 0) { return true; }
            }

            return false;
        }

        /// <summary>
        /// 学校接口没给出当前周时的兜底：拿**上次成功时的周次**，按"两个日期所在周的周一差几周"往后推。
        ///
        /// 为什么不直接按天数除以 7：学校的周次是在**周一零点**切换的（实测过）。
        /// 缓存如果是周五存的，下周一再用，按天数算只差 3 天 → 算出来是同一周，实际已经差了一周。
        /// 按周一对齐就没有这个问题。取不到缓存时返回 0，由调用方决定怎么办。
        /// </summary>
        private static int FallbackWeek()
        {
            int cachedWeek;
            DateTime savedAt;
            if (!ScheduleCache.TryGetWeek(out cachedWeek, out savedAt)) { return 0; }

            var weeks = (int)((StartOfWeek(AppClock.Now.Date) - StartOfWeek(savedAt.Date)).TotalDays / 7);
            var week = cachedWeek + weeks;

            if (weeks > 0)
            {
                Logger.Info(string.Format(
                    "缓存里是第 {0} 周（{1:MM-dd} 存），距今 {2} 周，推算当前为第 {3} 周",
                    cachedWeek,
                    savedAt,
                    weeks,
                    week));
            }

            return week;
        }

        /// <summary>取某个日期所在那一周的周一（学校的周次按周一切换）。</summary>
        private static DateTime StartOfWeek(DateTime date)
        {
            // DayOfWeek：周日=0、周一=1 … 周六=6。这里统一到"本周的周一"。
            var offset = ((int)date.DayOfWeek + 6) % 7;
            return date.Date.AddDays(-offset);
        }

        /// <summary>带一次重试的 POST。接口失败时返回 HTTP 200 + result=0，真正的异常只在网络层。</summary>
        private DktkResponse Request(string week)
        {
            Exception last = null;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    return PostOnce(week);
                }
                catch (Exception ex)
                {
                    last = ex;
                    Logger.Warn(string.Format("课表接口请求失败（第 {0} 次）：{1}", attempt + 1, ex.Message));
                }
            }

            throw last ?? new InvalidOperationException("课表接口请求失败");
        }

        private DktkResponse PostOnce(string week)
        {
            var request = (HttpWebRequest)WebRequest.Create(_apiUrl);
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.Timeout = _timeoutSeconds * 1000;
            request.ReadWriteTimeout = _timeoutSeconds * 1000;
            request.UserAgent = "KeRing";
            request.KeepAlive = false;

            // 教室机器在内网直连教务服务器，绕开系统代理探测——否则每次请求可能白等几秒
            request.Proxy = null;

            // 直接写 Cookie 头，不用 CookieContainer：后者会对已经百分号编码过的值再处理一次。
            // 编码必须是 UTF-8，不能用 gb2312（参考项目的注释专门强调过）。
            request.Headers["Cookie"] = "usernameEncoded=" + Uri.EscapeDataString(_cookieUser);

            var body = string.Concat(
                "keAction=getKeViewInitData&keSpan=dktk&kWeek=",
                Uri.EscapeDataString(week));
            var payload = Encoding.UTF8.GetBytes(body);
            request.ContentLength = payload.Length;

            using (var stream = request.GetRequestStream())
            {
                stream.Write(payload, 0, payload.Length);
            }

            var sentUtc = DateTime.UtcNow;
            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var json = reader.ReadToEnd();
                var receivedUtc = DateTime.UtcNow;

                // 顺便把服务器时间报给时间基准——这是白捡的，不用多发一次请求
                ReportServerDate(response, sentUtc, receivedUtc);

                return JsonConvert.DeserializeObject<DktkResponse>(json);
            }
        }

        /// <summary>
        /// 把响应里的 Date 头报给 AppClock。
        /// **教务服务器的钟实测慢 14 秒**，所以 AppClock 只在偏差很大（本机钟明显坏了）时才采用它，
        /// 否则会把一个本来准的本机时钟带偏。详见 AppClock.ServerTrustThreshold。
        /// </summary>
        private static void ReportServerDate(HttpWebResponse response, DateTime sentUtc, DateTime receivedUtc)
        {
            try
            {
                var header = response.Headers["Date"];
                if (string.IsNullOrWhiteSpace(header)) { return; }

                DateTimeOffset serverTime;
                if (!DateTimeOffset.TryParse(
                        header,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out serverTime))
                {
                    return;
                }

                // 用往返的中点当"本机这一刻"，把一半网络延迟抵掉
                var midpoint = sentUtc.AddMilliseconds((receivedUtc - sentUtc).TotalMilliseconds / 2);
                AppClock.ReportServerTime(serverTime.UtcDateTime, midpoint);
            }
            catch (Exception ex)
            {
                Logger.Warn("解析响应 Date 头失败：" + ex.Message);
            }
        }

        private static string DescribeFailure(DktkResponse data)
        {
            if (data == null) { return "返回内容无法解析"; }
            return string.IsNullOrWhiteSpace(data.Message) ? "result=" + data.Result : data.Message;
        }

        private static int ToInt(object raw)
        {
            if (raw == null) { return 0; }
            if (raw is int) { return (int)raw; }
            if (raw is long) { return (int)(long)raw; }

            int value;
            return int.TryParse(
                Convert.ToString(raw, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value) ? value : 0;
        }

        /// <summary>接口返回体。字段名跟学校的 JSON 一一对应，这里只声明我们用得到的。</summary>
        private sealed class DktkResponse
        {
            [JsonProperty("result")]
            public int Result { get; set; }

            [JsonProperty("msg")]
            public string Message { get; set; }

            /// <summary>本次请求的周次。</summary>
            [JsonProperty("kWeek")]
            public int Week { get; set; }

            /// <summary>学校认定的当前周。</summary>
            [JsonProperty("curKWeek")]
            public int CurrentWeek { get; set; }

            [JsonProperty("knPerDay")]
            public int PeriodsPerDay { get; set; }

            [JsonProperty("knPerWeek")]
            public int SlotsPerWeek { get; set; }

            [JsonProperty("keTableData")]
            public List<DktkClass> Classes { get; set; }

            /// <summary>调/代/帮/预代课记录。第 0 行是空表头，数据从第 1 行起。</summary>
            [JsonProperty("dktkInfoAoa")]
            public List<List<object>> Adjustments { get; set; }

            /// <summary>换算出来的天数，只用来写日志。</summary>
            public int DaysPerWeek
            {
                get
                {
                    var perDay = PeriodsPerDay > 0 ? PeriodsPerDay : 6;
                    var perWeek = SlotsPerWeek > 0 ? SlotsPerWeek : perDay * MaxDays;
                    return Math.Max(1, Math.Min(MaxDays, perWeek / perDay));
                }
            }
        }

        private sealed class DktkClass
        {
            [JsonProperty("className")]
            public string Name { get; set; }

            /// <summary>按天铺平的一维格子：索引 = 天序号 × knPerDay + 节序号（都从 0 起）。</summary>
            [JsonProperty("keVals")]
            public List<object> Values { get; set; }
        }
    }
}

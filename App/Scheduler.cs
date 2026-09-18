using System;
using System.Collections.Generic;
using System.Timers;
using KeRing.App.Schedule;

namespace KeRing.App
{
    internal sealed class ReminderEventArgs : EventArgs
    {
        public ReminderPoint Point { get; set; }
    }

    /// <summary>
    /// 打铃调度：1 秒轮询，每次都用系统时钟重新比对打铃点（不累加间隔，避免漂移）。
    /// 已经过去的点直接跳过——当前约定不补播，只写日志。
    /// </summary>
    internal sealed class Scheduler : IDisposable
    {
        private readonly object _gate = new object();
        private readonly System.Timers.Timer _timer;
        /// <summary>提前提醒的档位（降序）。改它只走 SetAhead，保证顺序/去重一致。</summary>
        private readonly List<int> _aheadList = new List<int>();

        private WeekSchedule _schedule;
        private List<ReminderPoint> _pointsOfToday = new List<ReminderPoint>();
        private DateTime _pointsDate = DateTime.MinValue;

        public event EventHandler<ReminderEventArgs> ReminderDue;

        public Scheduler(IList<int> aheadMinutesList)
        {
            SetAhead(aheadMinutesList);
            _timer = new System.Timers.Timer(1000);
            _timer.AutoReset = true;
            _timer.Elapsed += OnElapsed;
        }

        public void Start()
        {
            _timer.Start();
        }

        public void UpdateSchedule(WeekSchedule schedule)
        {
            lock (_gate)
            {
                _schedule = schedule;
                Rebuild(AppClock.Now);
            }
        }

        /// <summary>改了档位就立刻按新规则重建当天的打铃点。</summary>
        public void UpdateAheadList(IList<int> aheadMinutesList)
        {
            lock (_gate)
            {
                SetAhead(aheadMinutesList);
                if (_schedule != null) { Rebuild(AppClock.Now); }
            }
        }

        /// <summary>存一份档位（去重、大的在前）。调用方给的列表可能被改，所以复制一份。</summary>
        private void SetAhead(IList<int> source)
        {
            _aheadList.Clear();
            if (source == null) { return; }

            foreach (var value in source)
            {
                if (value < 0 || value > 60) { continue; }
                if (_aheadList.Contains(value)) { continue; }
                _aheadList.Add(value);
            }

            _aheadList.Sort((a, b) => b.CompareTo(a));
        }

        /// <summary>下一个提醒点（今天没有就往后找），界面用。</summary>
        public ReminderPoint NextReminder
        {
            get
            {
                DateTime fireTime;
                lock (_gate)
                {
                    return ReminderPlanner.FindNext(_schedule, AppClock.Now, _aheadList, out fireTime);
                }
            }
        }

        private void Rebuild(DateTime now)
        {
            _pointsOfToday = ReminderPlanner.BuildForDay(_schedule, now, _aheadList);
            _pointsDate = now.Date;

            foreach (var point in _pointsOfToday)
            {
                if (point.Time <= now)
                {
                    point.Fired = true; // 程序启动前/重启期间过去的点不补播
                }
            }
        }

        private void OnElapsed(object sender, ElapsedEventArgs e)
        {
            List<ReminderPoint> due = null;

            try
            {
                var now = AppClock.Now;
                lock (_gate)
                {
                    if (_schedule == null) { return; }
                    if (now.Date != _pointsDate)
                    {
                        Rebuild(now); // 跨天：重新生成当天的打铃点
                        return;
                    }

                    foreach (var point in _pointsOfToday)
                    {
                        if (point.Fired || point.Time > now) { continue; }

                        point.Fired = true;
                        if (due == null) { due = new List<ReminderPoint>(); }
                        due.Add(point);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("调度异常", ex);
                return;
            }

            if (due == null) { return; }

            var handler = ReminderDue;
            if (handler == null) { return; }

            foreach (var point in due)
            {
                try
                {
                    handler(this, new ReminderEventArgs { Point = point });
                }
                catch (Exception ex)
                {
                    Logger.Error("处理提醒失败", ex);
                }
            }
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Elapsed -= OnElapsed;
            _timer.Dispose();
        }
    }
}

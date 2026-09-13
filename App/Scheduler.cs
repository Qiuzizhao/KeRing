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
        private int _aheadMinutes;

        private WeekSchedule _schedule;
        private List<ReminderPoint> _pointsOfToday = new List<ReminderPoint>();
        private DateTime _pointsDate = DateTime.MinValue;

        public event EventHandler<ReminderEventArgs> ReminderDue;

        public Scheduler(int aheadMinutes)
        {
            _aheadMinutes = aheadMinutes;
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
                Rebuild(DateTime.Now);
            }
        }

        /// <summary>改了提前量就立刻按新规则重建当天的打铃点。</summary>
        public void UpdateAheadMinutes(int minutes)
        {
            lock (_gate)
            {
                _aheadMinutes = minutes;
                if (_schedule != null) { Rebuild(DateTime.Now); }
            }
        }

        /// <summary>下一个提醒点（今天没有就往后找），界面用。</summary>
        public ReminderPoint NextReminder
        {
            get
            {
                DateTime fireTime;
                lock (_gate)
                {
                    return ReminderPlanner.FindNext(_schedule, DateTime.Now, _aheadMinutes, out fireTime);
                }
            }
        }

        private void Rebuild(DateTime now)
        {
            _pointsOfToday = ReminderPlanner.BuildForDay(_schedule, now, _aheadMinutes);
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
                var now = DateTime.Now;
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

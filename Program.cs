using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using KeRing.App;
using KeRing.UI;

namespace KeRing
{
    internal static class Program
    {
        private const string InstanceBaseName = "KeRing.SingleInstance";
        private const string ShowSignalBaseName = "KeRing.ShowWindow";

        /// <summary>命名空间前缀：能建全局对象就是 "Global\"，否则退成本会话。</summary>
        private static string _namespacePrefix = string.Empty;

        [STAThread]
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && string.Equals(args[0], "--selftest", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = SelfTest.Run();
                return;
            }

            if (args != null && args.Length > 0 && string.Equals(args[0], "--say", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = SelfTest.Say(args.Length > 1 ? args[1] : "语文");
                return;
            }

            bool isFirstInstance;
            using (var mutex = AcquireMutex(out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    // 已经有实例在跑：通知它把窗口显示出来，自己退出
                    Logger.Info("已有实例在运行，本次启动直接退出（单实例保护）");
                    SignalExistingInstance();
                    return;
                }

                EnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Logger.Cleanup();
                var config = AppConfig.Load();
                Logger.Info(string.Format(
                    "程序启动，版本 {0}，单实例锁：{1}",
                    typeof(Program).Assembly.GetName().Version,
                    _namespacePrefix.Length == 0 ? "本会话" : "全局（跨会话）"));

                using (var showSignal = CreateShowSignal())
                using (var mainForm = new MainForm(config, showSignal))
                {
                    Application.Run(mainForm);
                }

                Logger.Info("程序退出");
            }
        }

        /// <summary>
        /// 拿单实例锁。优先用 Global\ 命名空间：同一个账号在别的会话（比如远程桌面）再登录时，
        /// HKCU 的自启项会在那个会话里再启动一个实例，本会话内的锁挡不住它，会出现两个实例同时打铃。
        /// 标准用户没有创建全局对象的权限，拿不到就退回本会话的锁。
        /// </summary>
        private static Mutex AcquireMutex(out bool isFirstInstance)
        {
            try
            {
                var global = new Mutex(true, @"Global\" + InstanceBaseName, out isFirstInstance);
                _namespacePrefix = @"Global\";
                return global;
            }
            catch (Exception ex)
            {
                Logger.Warn("无法创建全局单实例锁，退回本会话锁：" + ex.Message);
            }

            _namespacePrefix = string.Empty;
            return new Mutex(true, InstanceBaseName, out isFirstInstance);
        }

        private static EventWaitHandle CreateShowSignal()
        {
            return new EventWaitHandle(false, EventResetMode.AutoReset, _namespacePrefix + ShowSignalBaseName);
        }

        private static void SignalExistingInstance()
        {
            try
            {
                using (var signal = EventWaitHandle.OpenExisting(_namespacePrefix + ShowSignalBaseName))
                {
                    signal.Set();
                }
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                MessageBox.Show("程序已经在运行。", "智能课表打铃", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static void EnableDpiAwareness()
        {
            try
            {
                SetProcessDPIAware();
            }
            catch (Exception ex)
            {
                Logger.Warn("设置 DPI 感知失败：" + ex.Message);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }
}

using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace KeRing.App
{
    /// <summary>
    /// 开机自启：写 HKCU\...\Run，不需要管理员权限。
    /// 前提是目标机开启自动登录，否则登录前不会执行。
    /// </summary>
    internal static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "KeRing";

        /// <summary>当前 exe 应该写进注册表的那一条命令。</summary>
        public static string BuildCommand()
        {
            return "\"" + Application.ExecutablePath + "\"";
        }

        /// <summary>读取注册表里已登记的命令；没有则返回 null。</summary>
        public static string GetRegisteredCommand()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) { return null; }
                    var value = key.GetValue(ValueName) as string;
                    return string.IsNullOrWhiteSpace(value) ? null : value;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("读取自启命令失败：" + ex.Message);
                return null;
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    return key != null && key.GetValue(ValueName) != null;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn("读取自启状态失败：" + ex.Message);
                return false;
            }
        }

        public static bool SetEnabled(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null)
                    {
                        Logger.Warn("打不开 HKCU Run 项，自启设置失败");
                        return false;
                    }

                    if (enabled)
                    {
                        key.SetValue(ValueName, BuildCommand(), RegistryValueKind.String);
                        Logger.Info("已开启开机自启：" + Application.ExecutablePath);
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                        Logger.Info("已关闭开机自启");
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("设置开机自启失败", ex);
                return false;
            }
        }
    }
}

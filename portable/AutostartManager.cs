using System;
using Microsoft.Win32;

namespace QuickCal
{
    public enum AutostartState
    {
        Enabled,
        Disabled,
        /// <summary>The user turned QuickCal off in Task Manager's "Startup apps" tab.</summary>
        DisabledInTaskManager
    }

    /// <summary>
    /// Replaces the UWP StartupTask.
    /// Uses HKEY_CURRENT_USER\...\Run, which affects only the current user and needs no admin rights.
    /// The entry shows up (and can be turned off) in Task Manager's "Startup apps" tab, just like before.
    /// </summary>
    public static class AutostartManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ValueName = "QuickCal";

        private static string Command => $"\"{Environment.ProcessPath}\" {App.TrayArgument}";

        public static AutostartState GetState()
        {
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKeyPath))
            {
                if (run?.GetValue(ValueName) == null) return AutostartState.Disabled;
            }

            // Task Manager stores its on/off switch here. First byte: even = enabled, odd = disabled.
            using (RegistryKey approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath))
            {
                if (approved?.GetValue(ValueName) is byte[] data && data.Length > 0 && (data[0] & 1) == 1)
                {
                    return AutostartState.DisabledInTaskManager;
                }
            }
            return AutostartState.Enabled;
        }

        public static void Enable()
        {
            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true))
            {
                run.SetValue(ValueName, Command, RegistryValueKind.String);
            }
        }

        public static void Disable()
        {
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
            {
                run?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            using (RegistryKey approved = Registry.CurrentUser.OpenSubKey(ApprovedKeyPath, writable: true))
            {
                approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }

        /// <summary>If the .exe has been moved, point the existing autostart entry at the new location.</summary>
        public static void RefreshPathIfEnabled()
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true))
                {
                    if (run?.GetValue(ValueName) is string current &&
                        !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
                    {
                        run.SetValue(ValueName, Command, RegistryValueKind.String);
                    }
                }
            }
            catch (Exception)
            {
                // Not critical; the user can toggle autostart again from Settings.
            }
        }
    }
}

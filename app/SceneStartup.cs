using System;
using System.IO;
using Microsoft.Win32;

namespace TwinShift
{
    internal static class SceneStartup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Vedal AI Twin Shift";

        internal static void Set(bool enabled)
        {
            if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("TWINSHIFT_DATA_DIR")) ||
                !File.Exists(Path.Combine(Files.Base, "twinshift.install")) ||
                File.Exists(Path.Combine(Files.Base, "portable.install"))) return;

            string command = "\"" + Path.Combine(Files.Base, "TwinShift.exe") + "\" --tray";
            if (enabled)
            {
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
                    if (!String.Equals(key.GetValue(Name) as string, command, StringComparison.OrdinalIgnoreCase))
                        key.SetValue(Name, command);
            }
            else
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                    if (String.Equals(key?.GetValue(Name) as string, command, StringComparison.OrdinalIgnoreCase))
                        key.DeleteValue(Name, false);
            }
        }
    }
}

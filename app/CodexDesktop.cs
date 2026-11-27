using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace TwinShift
{
    internal static class CodexDesktop
    {
        const string Family = "OpenAI.Codex_2p2nqsd0c76g0";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetPackageFamilyName(IntPtr process, ref uint length, StringBuilder family);

        internal static bool Matches(Process process)
        {
            if (string.Equals(process.ProcessName, "Codex", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.Equals(process.ProcessName, "ChatGPT", StringComparison.OrdinalIgnoreCase))
                return false;

            var family = new StringBuilder(256);
            uint length = (uint)family.Capacity;
            int result = GetPackageFamilyName(process.Handle, ref length, family);
            if (result == 0)
                return string.Equals(family.ToString(), Family, StringComparison.OrdinalIgnoreCase);

            // An independently started desktop instance can lack package identity.
            const int NoPackage = 15700;
            string apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            return result == NoPackage && IsInstalledImage(process.MainModule.FileName, apps);
        }

        internal static bool IsInstalledImage(string imagePath, string windowsApps)
        {
            var image = new FileInfo(imagePath);
            var app = image.Directory;
            var package = app?.Parent;
            return string.Equals(image.Name, "ChatGPT.exe", StringComparison.OrdinalIgnoreCase)
                && string.Equals(app?.Name, "app", StringComparison.OrdinalIgnoreCase)
                && string.Equals(package?.Parent?.FullName, windowsApps, StringComparison.OrdinalIgnoreCase)
                && package.Name.StartsWith("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase)
                && package.Name.EndsWith("__2p2nqsd0c76g0", StringComparison.OrdinalIgnoreCase);
        }
    }
}

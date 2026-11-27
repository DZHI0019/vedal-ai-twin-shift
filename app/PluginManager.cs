using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TwinShift
{
    internal static class PluginManager
    {
        const string Market = "twin-shift-local";
        const string Plugin = "twin-shift-events@" + Market;
        sealed class MarketItem
        {
            public string Name { get; set; }
            public string Root { get; set; }
        }

        sealed class MarketList
        {
            public MarketItem[] Marketplaces { get; set; }
        }

        sealed class PluginItem
        {
            public string PluginId { get; set; }
        }

        sealed class PluginList
        {
            public PluginItem[] Installed { get; set; }
        }

        static string Exe()
        {
            foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
            {
                string dir = entry.Trim().Trim('"');
                if (!Path.IsPathRooted(dir))
                    continue;
                string p = Path.Combine(dir, "codex.exe");
                if (File.Exists(p))
                    return p;
            }

            foreach (var proc in Process.GetProcessesByName("Codex").Concat(Process.GetProcessesByName("ChatGPT")))
                using (proc)
                {
                    try
                    {
                        if (!CodexDesktop.Matches(proc))
                            continue;
                        string dir = Path.GetDirectoryName(proc.MainModule.FileName);
                        string p = Path.Combine(dir, "resources", "codex.exe");
                        if (File.Exists(p))
                            return p;
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                    }
                    catch (InvalidOperationException)
                    {
                    }
                }

            throw new InvalidOperationException("找不到 Codex CLI。请打开已安装的 Codex 桌面版后重试。");
        }

        static async Task<(int code, string output)> Run(string exe, string args)
        {
            using (var proc = new Process
            {
                StartInfo = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    WorkingDirectory = Files.Base
                }
            }

            )
            {
                proc.Start();
                var stdout = proc.StandardOutput.ReadToEndAsync();
                var stderr = proc.StandardError.BaseStream.CopyToAsync(Stream.Null);
                bool done = await Task.Run(() => proc.WaitForExit(30000));
                if (!done)
                {
                    try
                    {
                        proc.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                    }

                    throw new TimeoutException("Codex CLI 超时，可在 Codex 的插件设置中检查后重试。");
                }

                await Task.WhenAll(stdout, stderr);
                return (proc.ExitCode, stdout.Result);
            }
        }

        internal static async Task<string> Change(bool enable)
        {
            string exe = Exe();
            string marker = Path.Combine(SignalBus.Root, "managed-marketplace.txt");
            string marketRoot = Path.Combine(Files.Base, "marketplace");
            var current = await Run(exe, "plugin marketplace list --json");
            var parser = new JavaScriptSerializer();
            if (current.code != 0)
                throw new InvalidOperationException("Codex 未返回市场列表，请检查 CLI 版本。");
            var markets = parser.Deserialize<MarketList>(current.output)?.Marketplaces ?? throw new InvalidDataException("无法识别 Codex 市场列表。");
            var registered = markets.FirstOrDefault(m => m.Name == Market);
            bool owns = File.Exists(marker) && String.Equals(File.ReadAllText(marker), marketRoot, StringComparison.OrdinalIgnoreCase);
            bool same = registered != null && String.Equals(registered.Root, marketRoot, StringComparison.OrdinalIgnoreCase);
            if (enable)
            {
                if (!File.Exists(Path.Combine(marketRoot, ".agents", "plugins", "marketplace.json")))
                    throw new InvalidOperationException("安装包的本地市场不完整。");
                if (registered != null && !same)
                    throw new InvalidOperationException("同名市场已指向其他目录。请先在 Codex 中确认并处理原有来源。");
                if (!same && (await Run(exe, "plugin marketplace add \"" + marketRoot + "\"")).code != 0)
                    throw new InvalidOperationException("Codex 未能登记此市场。请在插件设置中检查市场来源或 CLI 版本。");
                Files.AtomicText(marker, marketRoot);
                if ((await Run(exe, "plugin add " + Plugin)).code != 0)
                    throw new InvalidOperationException("市场已登记，事件插件尚未启用。可以重试或在 Codex 插件页安装。");
                return "已安装事件插件。在 Codex 的 Hooks 设置中审阅并信任，然后打开新聊天。";
            }

            if (!owns)
                return "本安装位置没有托管的市场。可在 Codex 设置中管理手动添加的来源。";
            if (!same)
            {
                File.Delete(marker);
                return "此来源已移除，本地登记已清理。";
            }

            var listed = await Run(exe, "plugin list --marketplace " + Market + " --json");
            if (listed.code != 0)
                throw new InvalidOperationException("无法确认插件状态，已保留市场以便重试。");
            var installed = parser.Deserialize<PluginList>(listed.output)?.Installed ?? throw new InvalidDataException("无法识别 Codex 插件列表。");
            if (installed.Any(p => p.PluginId == Plugin) && (await Run(exe, "plugin remove " + Plugin)).code != 0)
                throw new InvalidOperationException("Codex 未能停用事件插件，已保留市场。");
            if ((await Run(exe, "plugin marketplace remove " + Market)).code != 0)
                throw new InvalidOperationException("请在 Codex 的插件市场设置中确认来源已移除。");
            File.Delete(marker);
            return "事件插件和本地市场已移除。";
        }
    }
}

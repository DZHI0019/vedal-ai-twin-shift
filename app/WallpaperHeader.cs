using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace TwinShift
{
    internal static class WallpaperHeader
    {
        // These three pairs are the Windows output of Dream Skin's own asset generator
        // at 6fc42df, before and after extending the single header-tint selector.
        const string Legacy = "header:is(.app-header-tint, [data-app-shell-header-edge-scroll], [class*=\"_Header_\"])";
        const string Current = ":is(" + Legacy + ", header[data-app-shell-titlebar=\"true\"], " +
            "[data-app-shell-frame=\"true\"] > :is([class~=\"group/application-menu-top-bar\"], [class*=\"_ApplicationMenuTopBar_\"]), " +
            "[data-app-shell-frame][data-app-shell-application-menu-bar=\"true\"] [class~=\"h-toolbar-sm\"][class~=\"bg-(--header-tint)\"])";
        static readonly (string Name, string Original, string Updated)[] Assets =
        {
            ("dream-skin.css", "6cd518f4f4a2a17ccb7f4bfcf03ecf73fb3f74e9d8f3c9788751aba06f46c828", "db78faa2c7cbaa77425eba34ed3e51090ca8162ce8db60b49e3f6e25c741c11b"),
            ("renderer-inject.js", "576c52af236d251e815c3398a8a8da419287e1281ebf2e05718ddd9fadb248cc", "a96b4321e16eb6f2d07aadd8d873737d88b5ca8f13a53c83018f577e20eac070"),
            ("selectors.json", "939217a45f6502b9939a23f8eb6d1e766d306a94f462adfe38d9d50d0c191873", "c419fce7c798f3d13f66597c6aac6f5d1800cd53a9c86834a6008bb166ae10f5")
        };
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        static void CheckPath(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("壁纸宿主使用了重定向路径，请从原宿主管理器更新。");
        }

        static string ReadAsset(string path)
        {
            CheckPath(path);
            using (var file = File.OpenRead(path))
            {
                if (file.Length > 1024 * 1024)
                    throw new InvalidDataException("宿主文件超出已核验的大小。");
                using (var data = new BinaryReader(file))
                    return Utf8.GetString(data.ReadBytes(checked((int)file.Length)));
            }
        }

        internal static bool Change(string engine, bool enable)
        {
            string assets = Path.Combine(engine, "assets");
            string version = Path.Combine(engine, "VERSION");
            if (!File.Exists(version))
                throw new InvalidDataException("请先用 Dream Skin 安装 Windows 壁纸宿主。");
            CheckPath(Path.GetDirectoryName(engine));
            CheckPath(engine);
            CheckPath(assets);
            if (ReadAsset(version).Trim() != "1.5.21")
                throw new InvalidDataException("此兼容修复仅适用于 Dream Skin 1.5.21。");

            string name = @"Local\TwinShift-WallpaperHeader-" + SignalBus.Hash(Path.GetFullPath(engine).ToUpperInvariant());
            using (var guard = new Mutex(true, name, out bool acquired))
            {
                if (!acquired)
                    throw new IOException("另一实例正在更新壁纸宿主，请稍后重试。");
                var changes = new List<(string Path, string Before, string After)>();
                foreach (var asset in Assets)
                {
                    string path = Path.Combine(assets, asset.Name);
                    string before = ReadAsset(path);
                    string digest = SignalBus.Hash(before);
                    string expected = enable ? asset.Updated : asset.Original;
                    if (digest == expected)
                        continue;
                    if (digest != (enable ? asset.Original : asset.Updated))
                        throw new InvalidDataException("宿主文件与固定版本不一致：" + asset.Name);
                    string legacy = Legacy, current = Current;
                    if (asset.Name != "dream-skin.css")
                    {
                        legacy = legacy.Replace("\"", "\\\"");
                        current = current.Replace("\"", "\\\"");
                    }
                    string after = enable ? before.Replace(legacy, current) : before.Replace(current, legacy);
                    if (SignalBus.Hash(after) != expected)
                        throw new InvalidDataException("标题栏资源校验失败：" + asset.Name);
                    changes.Add((path, before, after));
                }

                // Each replace is atomic. A later run can finish or reverse any mix of
                // the two known versions after a process or power interruption.
                int applied = 0;
                try
                {
                    foreach (var change in changes)
                    {
                        if (ReadAsset(change.Path) != change.Before)
                            throw new IOException("壁纸宿主已变化，请重新检查版本。");
                        Files.AtomicText(change.Path, change.After);
                        applied++;
                    }
                }
                catch
                {
                    for (int i = applied - 1; i >= 0; i--)
                    {
                        var change = changes[i];
                        if (ReadAsset(change.Path) == change.After)
                            Files.AtomicText(change.Path, change.Before);
                    }
                    throw;
                }
                return changes.Count != 0;
            }
        }
    }
}

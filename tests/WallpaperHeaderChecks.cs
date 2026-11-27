using System;
using System.IO;
using System.Linq;

namespace TwinShift
{
    internal static class WallpaperHeaderChecks
    {
        static void Check(bool value, string message)
        {
            if (!value)
                throw new InvalidDataException(message);
        }

        internal static void Run(string upstream)
        {
            string root = Path.Combine(SignalBus.Root, "wallpaper-engine");
            string assets = Path.Combine(root, "assets");
            Directory.CreateDirectory(assets);
            File.Copy(Path.Combine(upstream, "VERSION"), Path.Combine(root, "VERSION"));
            string[] names = { "dream-skin.css", "renderer-inject.js", "selectors.json" };
            foreach (string name in names)
                File.Copy(Path.Combine(upstream, "assets", name), Path.Combine(assets, name));
            string[] Snapshot() => names.Select(name => Convert.ToBase64String(File.ReadAllBytes(Path.Combine(assets, name)))).ToArray();
            var original = Snapshot();
            Check(WallpaperHeader.Change(root, true), "Apply to the stock host");
            var updated = Snapshot();
            Check(!WallpaperHeader.Change(root, true), "Repeated apply must be a no-op");
            File.Copy(Path.Combine(upstream, "assets", names[1]), Path.Combine(assets, names[1]), true);
            Check(WallpaperHeader.Change(root, true) && updated.SequenceEqual(Snapshot()), "Recover partial application");
            Check(WallpaperHeader.Change(root, false) && original.SequenceEqual(Snapshot()), "Restore upstream assets byte for byte");
            Check(!WallpaperHeader.Change(root, false), "Repeated restore must be a no-op");
            using (File.OpenRead(Path.Combine(assets, names[1])))
            {
                try
                {
                    WallpaperHeader.Change(root, true);
                    throw new Exception("Locked host file replaced");
                }
                catch (IOException)
                {
                    Check(original.SequenceEqual(Snapshot()), "Roll back a partial update when a file is locked");
                }
            }
            File.AppendAllText(Path.Combine(assets, names.Last()), " ");
            var unrecognized = Snapshot();
            try
            {
                WallpaperHeader.Change(root, true);
                throw new Exception("Unrecognized host accepted");
            }
            catch (InvalidDataException)
            {
                Check(unrecognized.SequenceEqual(Snapshot()), "Preflight before changing any file");
            }
        }
    }
}

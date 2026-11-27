using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace TwinShift
{
    internal static class Files
    {
        public static readonly string Base = AppDomain.CurrentDomain.BaseDirectory;
        public static string Asset(string file) => Path.Combine(Base, "assets", file);
        public static T ReadJson<T>(string file, int maxBytes)
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                if (stream.Length > maxBytes)
                    throw new InvalidDataException("文件过大");
                var data = new byte[checked((int)stream.Length)];
                int at = 0;
                while (at < data.Length)
                {
                    int n = stream.Read(data, at, data.Length - at);
                    if (n == 0)
                        throw new EndOfStreamException();
                    at += n;
                }

                return new JavaScriptSerializer
                {
                    MaxJsonLength = maxBytes,
                    RecursionLimit = 16
                }.Deserialize<T>(new UTF8Encoding(false, true).GetString(data).TrimStart('\uFEFF'));
            }
        }

        public static void AtomicText(string path, string text)
        {
            string dir = Path.GetDirectoryName(path);
            Directory.CreateDirectory(dir);
            string tmp = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.WriteAllText(tmp, text, new UTF8Encoding(false));
                if (File.Exists(path))
                    File.Replace(tmp, path, null);
                else
                    File.Move(tmp, path);
            }
            finally
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
        }
    }

    internal sealed class Preferences
    {
        public int Version { get; set; } = 1;
        public string Mode { get; set; } = "system";
        public int Volume { get; set; } = 60;
        public bool AutoVoice { get; set; }
        public bool Attention { get; set; }
        public bool Online { get; set; }
        public bool Badge { get; set; }
        public bool Muted { get; set; }
        public bool QuietMotion { get; set; }
        internal static string FileName => Path.Combine(SignalBus.Root, "settings.json");

        internal static Preferences Load(out string warning)
        {
            warning = "";
            if (!File.Exists(FileName))
                return new Preferences();
            try
            {
                var p = Files.ReadJson<Preferences>(FileName, 16384);
                if (p == null || p.Version != 1 || !new[]
                {
                    "neo",
                    "evo",
                    "system"
                }.Contains(p.Mode) || p.Volume < 0 || p.Volume > 100)
                    throw new InvalidDataException();
                return p;
            }
            catch (Exception)
            {
                warning = "设置无法读取，本次采用默认值。保存新偏好前将保留原文件副本。";
                return new Preferences();
            }
        }

        internal void Save() => Files.AtomicText(FileName, new JavaScriptSerializer().Serialize(this));
        internal bool Night
        {
            get
            {
                if (Mode != "system")
                    return Mode == "evo";
                try
                {
                    return Convert.ToInt32(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1)) == 0;
                }
                catch
                {
                    return false;
                }
            }
        }
    }

    internal sealed class Cue
    {
        public string Id { get; set; }
        public string Speaker { get; set; }
        public string Line { get; set; }
        public string Translation { get; set; }
        public string Slot { get; set; }
        public double Seconds { get; set; }
        public double Gain { get; set; }
        public string Url { get; set; }
        public string Sha256 { get; set; }
        public string Source { get; set; }
        public string Credit { get; set; }
    }
}

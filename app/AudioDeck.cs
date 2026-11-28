using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace TwinShift
{
    internal sealed class AudioDeck : IDisposable
    {
        internal readonly Dictionary<string, Cue> Clips;
        readonly Preferences settings;
        readonly VoiceOutput output = new VoiceOutput();
        CancellationTokenSource pending;
        double trim = 1;
        int serial;
        public event Action<string> Notice;
        internal event Action<string> Started;

        public AudioDeck(Preferences settings)
        {
            this.settings = settings;
            Clips = Files.ReadJson<Cue[]>(Files.Asset("voices.json"), 32768).ToDictionary(c => c.Id, StringComparer.Ordinal);
            string[] ids = { "yay", "win", "filtered", "help", "shucks", "pipes", "tutel", "vedal" };
            if (Clips.Count != ids.Length || !ids.All(Clips.ContainsKey))
                throw new InvalidDataException("角色语音目录不完整。");
            foreach (var c in Clips.Values)
            {
                if (!Regex.IsMatch(c.Sha256 ?? "", @"\A[a-f0-9]{64}\z") || !(c.Gain > 0 && c.Gain <= 1))
                    throw new InvalidDataException("角色语音目录无效。");
            }

            ClearTemporary();
        }

        internal static string Digest(byte[] data)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        internal void SetLevel()
        {
            output.SetVolume((float)Math.Max(0, Math.Min(1, trim * settings.Volume / 100)));
        }

        internal void Stop()
        {
            serial++;
            pending?.Cancel();
        }

        string LocalFile(Cue cue) => Path.Combine(SignalBus.Root, "voices", cue.Id + ".wav");
        void ClearTemporary()
        {
            string temporary = Path.Combine(SignalBus.Root, "preview-cache");
            // Only old releases used a disk cache. Cleanup is best-effort.
            try
            {
                if (!Directory.Exists(temporary) || (File.GetAttributes(temporary) & FileAttributes.ReparsePoint) != 0)
                    return;
                foreach (var cue in Clips.Values)
                    File.Delete(Path.Combine(temporary, cue.Id + ".mp3"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static byte[] ReadBounded(string path, int limit, string invalid)
        {
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                if (input.Length == 0 || input.Length > limit)
                    throw new InvalidDataException(invalid);
                using (var reader = new BinaryReader(input))
                    return reader.ReadBytes((int)input.Length);
            }
        }

        static byte[] ReadWave(string file) => ReadBounded(file, 4 * 1024 * 1024, "请选择 4 MB 以内的 PCM WAV。");

        internal static byte[] ReadBundled(Cue cue)
        {
            string path = Files.Asset(Path.Combine("voices", cue.Id + ".mp3"));
            byte[] data = ReadBounded(path, 1400000, "内置语音大小异常，请重新安装。");
            if (Digest(data) != cue.Sha256)
                throw new InvalidDataException("内置语音校验失败，请重新安装。");
            return data;
        }

        public void Import(Cue cue, string file)
        {
            byte[] bytes = ReadWave(file);
            if (!ValidWave(bytes))
                throw new InvalidDataException("需要 0–10 秒的单声道或立体声 PCM WAV（8 或 16 位）。");
            Stop();
            string dest = LocalFile(cue);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            string tmp = dest + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, bytes);
                if (File.Exists(dest))
                    File.Replace(tmp, dest, null);
                else
                    File.Move(tmp, dest);
            }
            finally
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
        }

        public bool HasLocal(Cue cue) => File.Exists(LocalFile(cue));
        public void RemoveLocal(Cue cue)
        {
            Stop();
            File.Delete(LocalFile(cue));
        }

        internal static bool ValidWave(byte[] b)
        {
            if (b == null || b.Length < 44 || BitConverter.ToUInt32(b, 0) != 0x46464952 || BitConverter.ToUInt32(b, 8) != 0x45564157 || BitConverter.ToUInt32(b, 4) != b.Length - 8)
                return false;
            int index = 12, rate = 0, align = 0, bytes = 0;
            while (index <= b.Length - 8)
            {
                uint tag = BitConverter.ToUInt32(b, index), len = BitConverter.ToUInt32(b, index + 4);
                int offset = index + 8;
                if (len > b.Length - offset)
                    return false;
                if (tag == 0x20746d66)
                {
                    if (len < 16 || rate != 0 || BitConverter.ToUInt16(b, offset) != 1)
                        return false;
                    int channels = BitConverter.ToUInt16(b, offset + 2), bits = BitConverter.ToUInt16(b, offset + 14);
                    rate = BitConverter.ToInt32(b, offset + 4);
                    align = BitConverter.ToUInt16(b, offset + 12);
                    if ((channels != 1 && channels != 2) || (bits != 8 && bits != 16) || rate < 8000 || rate > 96000 || align != channels * bits / 8 || BitConverter.ToUInt32(b, offset + 8) != rate * align)
                        return false;
                }

                if (tag == 0x61746164)
                {
                    if (bytes != 0 || rate == 0)
                        return false;
                    bytes = (int)len;
                }

                index = offset + (int)len + (int)(len % 2);
            }

            return index == b.Length && rate > 0 && bytes > 0 && bytes % align == 0 && bytes <= rate * align * 10;
        }

        public async Task<bool> Play(string id)
        {
            Stop();
            int mine = serial;
            if (settings.Volume == 0)
            {
                Notice?.Invoke("音量为零。");
                return false;
            }

            var request = new CancellationTokenSource();
            pending = request;
            try
            {
                var token = request.Token;
                request.CancelAfter(TimeSpan.FromSeconds(15));
                Cue cue = null;
                if (id != "attention" && !Clips.TryGetValue(id, out cue))
                    return false;
                bool bundled = cue != null && !HasLocal(cue);
                trim = bundled ? cue.Gain : 1;
                using (var clip = await Task.Run(() =>
                {
                    byte[] data = bundled ? ReadBundled(cue) : ReadWave(cue == null ? Files.Asset("attention.wav") : LocalFile(cue));
                    return VoiceOutput.Decode(data, !bundled, token);
                }, token))
                {
                    token.ThrowIfCancellationRequested();
                    SetLevel();
                    await output.Play(clip, token, () => Started?.Invoke(id));
                    return mine == serial;
                }
            }
            catch (OperationCanceledException)
            {
                if (mine == serial)
                    Notice?.Invoke("播放超时，请检查默认输出设备。");
                return false;
            }
            catch (Exception e)
            {
                if (mine == serial)
                    Notice?.Invoke(e is InvalidDataException || e is InvalidOperationException ? e.Message :
                        e is FileNotFoundException || e is DirectoryNotFoundException ? "找不到音频文件。请重新导入这句录音，或修复安装。" :
                        e is UnauthorizedAccessException ? "无法读取这句录音。请检查文件访问权限。" :
                        e is COMException ? "Windows 无法使用当前输出设备。请检查音量混合器或重新连接耳机。" :
                        "播放未完成。请检查输出设备，或重新导入这句录音。");
                return false;
            }
            finally
            {
                if (pending == request)
                    pending = null;
                request.Dispose();
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows;

namespace TwinShift
{
    internal sealed class AudioDeck : IDisposable
    {
        const int Limit = 1400000;
        internal readonly Dictionary<string, Cue> Clips;
        readonly HttpClient client = CreateClient();
        static HttpClient CreateClient()
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                UseDefaultCredentials = false
            };
            // .NET Framework uses Windows Internet Options by default. Also support an explicitly configured proxy
            // for the two public HTTPS audio origins in managed desktop and CI environments.
            string setting = Environment.GetEnvironmentVariable("HTTPS_PROXY");
            if (Uri.TryCreate(setting, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https") && uri.UserInfo.Length == 0)
            {
                string bypass = Environment.GetEnvironmentVariable("NO_PROXY") ?? "";
                var hosts = bypass.Split(',').Select(x => x.Trim().TrimStart('.')).Where(x => x.Length > 0).ToArray();
                if (hosts.Contains("*"))
                    handler.UseProxy = false;
                else
                    handler.Proxy = new WebProxy(uri, false, hosts.Select(h => @"(?:^|[./])" + Regex.Escape(h) + @"(?::\d+)?$").ToArray());
            }

            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        readonly string temporary = Path.Combine(SignalBus.Root, "preview-cache");
        readonly Preferences settings;
        readonly MediaPlayer player = new MediaPlayer();
        CancellationTokenSource pending;
        double trim = 1;
        int serial;
        public event Action<string> Notice;
        internal event Action<Exception> PlaybackError;
        public AudioDeck(Preferences settings)
        {
            this.settings = settings;
            Clips = Files.ReadJson<Cue[]>(Files.Asset("voices.json"), 32768).ToDictionary(c => c.Id, StringComparer.Ordinal);
            string[] ids =
            {
                "yay",
                "win",
                "filtered",
                "help",
                "shucks",
                "pipes",
                "tutel",
                "vedal"
            };
            if (Clips.Count != ids.Length || !ids.All(Clips.ContainsKey))
                throw new InvalidDataException("角色语音目录不完整。");
            foreach (var c in Clips.Values)
            {
                if (!Allowed(c.Url) || !Regex.IsMatch(c.Sha256 ?? "", @"\A[a-f0-9]{64}\z") || !(c.Gain > 0 && c.Gain <= 1))
                    throw new InvalidDataException("角色语音目录无效。");
            }

            ClearTemporary();
            player.MediaFailed += (s, e) => Notice?.Invoke("Windows 无法解码此音频。可换用本地 PCM WAV。");
        }

        internal static bool Allowed(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme != "https" || !u.IsDefaultPort || u.UserInfo != "" || u.Query != "" || u.Fragment != "")
                return false;
            return (u.Host == "storage.neurokaraoke.com" && u.AbsolutePath.StartsWith("/soundbites/", StringComparison.Ordinal)) || (u.Host == "minhtridotnet.github.io" && u.AbsolutePath.StartsWith("/neuro-sama-soundboard/Sounds/", StringComparison.Ordinal));
        }

        internal static string Digest(byte[] data)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        internal void SetLevel()
        {
            player.Volume = Math.Max(0, Math.Min(1, trim * settings.Volume / 100));
        }

        internal Duration MediaDuration => player.NaturalDuration;

        internal bool MutedPlayback
        {
            set
            {
                player.IsMuted = value;
            }
        }

        internal void Stop()
        {
            serial++;
            pending?.Cancel();
            player.Close();
        }

        string LocalFile(Cue cue) => Path.Combine(SignalBus.Root, "voices", cue.Id + ".wav");
        string CachedFile(Cue cue) => Path.Combine(temporary, cue.Id + ".mp3");
        void ClearTemporary()
        {
            if (!Directory.Exists(temporary) || (File.GetAttributes(temporary) & FileAttributes.ReparsePoint) != 0)
                return;
            foreach (var cue in Clips.Values)
            {
                try
                {
                    File.Delete(CachedFile(cue));
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        public void ClearOnline()
        {
            Stop();
            ClearTemporary();
        }

        public void Import(Cue cue, string file)
        {
            byte[] bytes;
            using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length < 44 || input.Length > 4 * 1024 * 1024)
                    throw new InvalidDataException("请选择 4 MB 以内的 PCM WAV。");
                using (var r = new BinaryReader(input))
                    bytes = r.ReadBytes((int)input.Length);
            }

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

        async Task<string> Fetch(Cue cue, CancellationToken token)
        {
            string cached = CachedFile(cue);
            if (File.Exists(cached) && new FileInfo(cached).Length <= Limit && Digest(File.ReadAllBytes(cached)) == cue.Sha256)
                return cached;
            using (var response = await client.GetAsync(cue.Url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > Limit)
                    throw new InvalidDataException("来源片段超过大小限制。");
                if (response.Content.Headers.ContentType?.MediaType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) != true)
                    throw new InvalidDataException("来源未返回音频内容。");
                using (var src = await response.Content.ReadAsStreamAsync())
                using (var mem = new MemoryStream())
                {
                    byte[] chunk = new byte[16384];
                    int n;
                    while ((n = await src.ReadAsync(chunk, 0, chunk.Length, token)) > 0)
                    {
                        if (mem.Length + n > Limit)
                            throw new InvalidDataException();
                        mem.Write(chunk, 0, n);
                    }

                    byte[] data = mem.ToArray();
                    token.ThrowIfCancellationRequested();
                    if (Digest(data) != cue.Sha256)
                        throw new InvalidDataException("来源音频已变化，请等待目录更新，或导入本地文件。");
                    Directory.CreateDirectory(temporary);
                    File.WriteAllBytes(cached, data);
                    return cached;
                }
            }
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

            var request = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            pending = request;
            try
            {
                string file;
                trim = 1;
                if (id == "attention")
                    file = Files.Asset("attention.wav");
                else
                {
                    if (!Clips.TryGetValue(id, out var cue))
                        return false;
                    if (HasLocal(cue))
                        file = LocalFile(cue);
                    else if (settings.Online)
                    {
                        trim = cue.Gain;
                        file = await Fetch(cue, request.Token);
                    }
                    else
                    {
                        Notice?.Invoke("请在「原声」开启个人联机试听，或为此台词导入本地 WAV。");
                        return false;
                    }
                }

                if (mine != serial || request.IsCancellationRequested)
                    return false;
                SetLevel();
                player.Open(new Uri(file));
                player.Play();
                return true;
            }
            catch (OperationCanceledException)
            {
                if (mine == serial)
                    Notice?.Invoke("取音超时，可以重试或使用本地 WAV。");
                return false;
            }
            catch (Exception e)
            {
                PlaybackError?.Invoke(e);
                if (mine == serial)
                    Notice?.Invoke(e is InvalidDataException && e.Message.StartsWith("来源") ? e.Message : e is HttpRequestException ? "无法从音频来源取得片段。请检查网络或代理，也可使用本地 WAV。" : "这段声音暂时无法播放，可以重试或使用本地 WAV。");
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
            client.Dispose();
            ClearTemporary();
        }
    }
}

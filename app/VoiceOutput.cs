using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NLayer;

namespace TwinShift
{
    internal sealed class VoiceOutput
    {
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        VolumeSampleProvider gain;
        float volume;

        internal void SetVolume(float value)
        {
            volume = value;
            if (gain != null)
                gain.Volume = value;
        }

        internal static WaveStream Decode(byte[] data, bool wave, CancellationToken token)
        {
            if (wave)
            {
                if (!AudioDeck.ValidWave(data))
                    throw new InvalidDataException("本地录音已损坏或格式不受支持。请重新导入 PCM WAV。");
                return new WaveFileReader(new MemoryStream(data, false));
            }

            using (var compressed = new MemoryStream(data, false))
            using (var mp3 = new MpegFile(compressed))
            {
                if (mp3.Channels < 1 || mp3.Channels > 2 || mp3.SampleRate < 8000 || mp3.SampleRate > 96000)
                    throw new InvalidDataException("线上片段的采样格式不受支持。");
                var format = WaveFormat.CreateIeeeFloatWaveFormat(mp3.SampleRate, mp3.Channels);
                using (var pcm = new MemoryStream())
                {
                    var samples = new float[4096];
                    var block = new byte[samples.Length * sizeof(float)];
                    int count;
                    while ((count = mp3.ReadSamples(samples, 0, samples.Length)) > 0)
                    {
                        token.ThrowIfCancellationRequested();
                        int size = count * sizeof(float);
                        if (pcm.Length + size > format.AverageBytesPerSecond * 10L)
                            throw new InvalidDataException("线上片段超过十秒。");
                        Buffer.BlockCopy(samples, 0, block, 0, size);
                        pcm.Write(block, 0, size);
                    }
                    if (pcm.Length == 0)
                        throw new InvalidDataException("线上片段没有可播放的音频。");
                    var source = new MemoryStream(pcm.GetBuffer(), 0, (int)pcm.Length, false);
                    return new RawSourceWaveStream(source, format);
                }
            }
        }

        internal async Task Play(WaveStream clip, CancellationToken token, Action started)
        {
            await gate.WaitAsync(token);
            try
            {
                using (var devices = new MMDeviceEnumerator())
                {
                    if (!devices.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                        throw new InvalidOperationException("找不到 Windows 默认输出设备。连接耳机或音箱后再试。");
                    using (var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                    using (var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 60))
                    {
                        if (device.AudioEndpointVolume.Mute || device.AudioEndpointVolume.MasterVolumeLevelScalar <= 0)
                            throw new InvalidOperationException("Windows 输出设备已静音。请检查系统音量。");
                        gain = new VolumeSampleProvider(clip.ToSampleProvider()) { Volume = volume };
                        output.Init(gain.ToWaveProvider());
                        if (SessionMuted(device))
                            throw new InvalidOperationException("Vedal AI 在 Windows 音量混合器中已静音。请检查应用音量。");
                        var ended = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        output.PlaybackStopped += (sender, e) =>
                        {
                            if (e.Exception != null)
                                ended.TrySetException(e.Exception);
                            else
                                ended.TrySetResult(true);
                        };
                        using (token.Register(() => ended.TrySetCanceled()))
                        {
                            token.ThrowIfCancellationRequested();
                            output.Play();
                            started();
                            await ended.Task;
                            token.ThrowIfCancellationRequested();
                            if (clip.Position < clip.Length)
                                throw new IOException("音频设备在播放结束前停止。");
                        }
                    }
                }
            }
            finally
            {
                gain = null;
                gate.Release();
            }
        }

        static bool SessionMuted(MMDevice device)
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                        using (var session = sessions[i])
                            if (session.GetProcessID == process.Id)
                                return session.SimpleAudioVolume.Mute || session.SimpleAudioVolume.Volume <= 0;
                }
            }
            catch (COMException) { } // Some render drivers do not expose session volume.
            return false;
        }
    }
}

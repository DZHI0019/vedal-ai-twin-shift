using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Web.Script.Serialization;
using NAudio.CoreAudioApi;
using System.Net;
using System.Windows.Automation;

namespace TwinShift
{
    class Checks
    {
        static void Require(bool value, string message)
        {
            if (!value)
                throw new Exception(message);
        }

        static void Pump(int ms)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(ms)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                frame.Continue = false;
            };
            timer.Start();
            Dispatcher.PushFrame(frame);
        }

        static void Hook(string name, string tool, string command, string result, string id, bool includeBom = false)
        {
            var data = new
            {
                session_id = "session-private-TEST",
                turn_id = "t",
                tool_use_id = id,
                hook_event_name = name,
                tool_name = tool,
                tool_input = new
                {
                    command
                },
                tool_response = result
            };
            string root = Path.Combine(Files.Base, "marketplace", "plugin");
            var manifest = Files.ReadJson<Dictionary<string, Dictionary<string, HookGroup[]>>>(Path.Combine(root, "hooks", "hooks.json"), 16384);
            var commandLine = manifest["hooks"][name][0].Hooks[0].CommandWindows;
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /s /c \"" + commandLine + "\"")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.EnvironmentVariables["PLUGIN_ROOT"] = root;
            using (var p = Process.Start(start))
            {
                byte[] input = System.Text.Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(data));
                using (Stream stream = p.StandardInput.BaseStream)
                {
                    // .NET Framework may have already emitted the console encoding's preamble.
                    if (includeBom && p.StandardInput.Encoding.GetPreamble().Length == 0)
                    {
                        byte[] preamble = System.Text.Encoding.UTF8.GetPreamble();
                        stream.Write(preamble, 0, preamble.Length);
                    }
                    stream.Write(input, 0, input.Length);
                }
                Require(p.WaitForExit(3000) && p.ExitCode == 0 && p.StandardOutput.ReadToEnd() == "" && p.StandardError.ReadToEnd() == "", "A hook affected the conversation");
            }

            Pump(300);
        }

        sealed class HookGroup
        {
            public HookEntry[] Hooks { get; set; }
        }

        sealed class HookEntry
        {
            public string CommandWindows { get; set; }
        }

        static T Await<T>(Task<T> task)
        {
            var limit = Stopwatch.StartNew();
            while (!task.IsCompleted && limit.Elapsed.TotalSeconds < 60)
                Pump(20);
            Require(task.IsCompleted, "Operation timed out");
            return task.GetAwaiter().GetResult();
        }

        // Read only the test process's render-session level. No recording or microphone.
        static float MeasureOutput(Action start, Func<bool> finished, MMDevice device)
        {
            int ownPid = Process.GetCurrentProcess().Id;
            var clock = Stopwatch.StartNew();
            float peak = 0;
            AudioSessionControl session = null;
            try
            {
                start();
                do
                {
                    Pump(15);
                    if (session == null)
                    {
                        device.AudioSessionManager.RefreshSessions();
                        var sessions = device.AudioSessionManager.Sessions;
                        for (int i = 0; i < sessions.Count; i++)
                        {
                            var next = sessions[i];
                            if (next.GetProcessID == ownPid) { session = next; break; }
                            next.Dispose();
                        }
                    }
                    if (session != null)
                        peak = Math.Max(peak, session.AudioMeterInformation.MasterPeakValue);
                } while (!finished() && clock.Elapsed < TimeSpan.FromSeconds(35));
                Require(finished(), "Audio output timeout");
                Require(peak > 0.0001f, "No nonzero samples in this process's Windows output session");
                return peak;
            }
            finally { session?.Dispose(); }
        }

        sealed class OfflineProxy : IWebProxy
        {
            public int Attempts { get; private set; }
            public ICredentials Credentials { get; set; }
            public bool IsBypassed(Uri destination) => false;
            public Uri GetProxy(Uri destination)
            {
                Attempts++;
                throw new InvalidOperationException("Network access is disabled during offline playback tests.");
            }
        }

        static void Shot(MainWindow w, string root, string name, int width = 888, int height = 655)
        {
            var content = (FrameworkElement)w.Content;
            content.Measure(new Size(width, height));
            content.Arrange(new Rect(0, 0, width, height));
            content.UpdateLayout();
            var img = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            img.Render(content);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            using (var f = File.Create(Path.Combine(root, name + ".png")))
                enc.Save(f);
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--scene-host") return WindowSceneChecks.RunHost();
            if (args.Length == 3 && args[0] == "--chromium-scene")
            {
                try { ChromiumSceneChecks.Run(args[1], args[2]); return 0; }
                catch (Exception e) { Console.Error.WriteLine(e); return 1; }
            }
            if (args.Length == 1 && args[0] == "--window-scene")
            {
                try { WindowSceneChecks.Run(); return 0; }
                catch (Exception e) { Console.Error.WriteLine(e); return 1; }
            }
            if (args.Length < 1 || String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TWINSHIFT_DATA_DIR")))
                return 2;
            try
            {
                Directory.CreateDirectory(SignalBus.Root);
                string defaultData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TwinShift", "Data");
                foreach (string empty in new[] { null, "", "  " })
                    Require(SignalBus.ResolveDataDirectory(empty) == defaultData, "Empty data override uses the per-user directory");
                Require(SignalBus.ResolveDataDirectory(SignalBus.Root) == SignalBus.Root, "Explicit data directory is preserved");
                int engine = Array.IndexOf(args, "--wallpaper-engine");
                if (engine >= 0)
                    WallpaperHeaderChecks.Run(args[engine + 1]);
                string apps = Path.Combine(Path.GetTempPath(), "WindowsApps");
                string package = "OpenAI.Codex_1.2.3.4_x64__2p2nqsd0c76g0";
                Require(CodexDesktop.IsInstalledImage(Path.Combine(apps, package, "app", "ChatGPT.exe"), apps), "Packaged Codex");
                Require(!CodexDesktop.IsInstalledImage(Path.Combine(apps, "ChatGPT", "app", "ChatGPT.exe"), apps), "Other product");
                Require(!CodexDesktop.IsInstalledImage(Path.Combine(apps, "other", package, "app", "ChatGPT.exe"), apps), "Wrong install root");
                File.WriteAllText(Preferences.FileName, @"{""Version"":1,""Mode"":""evo"",""Volume"":73,""Online"":true}");
                var legacy = Preferences.Load(out string legacyWarning);
                Require(legacyWarning == "" && legacy.Volume == 73 && legacy.Mode == "evo", "Beta 3 preferences remain readable");
                File.Delete(Preferences.FileName);
                var offline = new OfflineProxy();
                WebRequest.DefaultWebProxy = offline;
                var app = new App();
                app.InitializeComponent();
                Pump(200);
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
                var win = (MainWindow)app.MainWindow;
                win.Hide();
                ((Button)win.FindName("NeoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(380);
                var copyNeo = (Button)win.FindName("CopyNeo");
                var copyEvo = (Button)win.FindName("CopyEvo");
                Require(copyNeo.Background == app.Resources["Accent"] && copyEvo.Background == app.Resources["Card"], "Neo primary action");
                Shot(win, args[0], "neo");
                var title = (TextBlock)win.FindName("LastTitle");
                var history = (StackPanel)win.FindName("History");
                Hook("PermissionRequest", "Bash", "sensitive text", "", "approval", includeBom: true);
                Require(title.Text.Contains("审批"), "Permission");
                Hook("PermissionRequest", "Bash", "sensitive text", "", "approval");
                Require(history.Children.Count == 1, "Duplicate event");
                Hook("Stop", "", "", "", "stop");
                Require(title.Text.Contains("结束"), "Stop is neutral: " + title.Text + ", entries=" + history.Children.Count);
                string report = "Running 2 tests using 1 worker\n  2 passed (1.2s)";
                Hook("PostToolUse", "Bash", "pnpm exec playwright test tests/task-list.spec.ts", report, "pw");
                Require(title.Text.Contains("通过"), "Playwright report");
                Hook("PostToolUse", "Bash", "echo test", report, "false");
                Require(history.Children.Count == 3, "Non-test must remain quiet");
                Hook("PostToolUse", "Bash", "node --test", "# tests 2\n# pass 1\n# fail 1\n# cancelled 0\n# duration_ms 54.1", "failed");
                Require(title.Text.Contains("失败"), "Node report");
                Hook("PostToolUse", "Bash", "node --test", "# tests 0\n# pass 0\n# fail 0\n# cancelled 0\n# duration_ms 20", "empty");
                Require(history.Children.Count == 4, "Empty suite");
                Hook("PostToolUse", "Bash", "node --test && echo hi", report, "compound");
                Require(history.Children.Count == 4, "Compound command");
                Hook("PreToolUse", "request_user_input", "", "", "q");
                Require(title.Text.Contains("补充"), "Question hook");
                Hook("Interrupt", "", "", "", "i");
                Require(title.Text.Contains("中断"), "Interrupt");
                string fixture = Path.Combine(args[0], "runner.test.cjs");
                File.WriteAllText(fixture, "require('node:test')('actual passing report', () => {});");
                using (var runner = Process.Start(new ProcessStartInfo("node.exe", "--test \"" + fixture + "\"") { UseShellExecute = false, RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.UTF8, CreateNoWindow = true }))
                {
                    string actual = runner.StandardOutput.ReadToEnd();
                    Require(runner.WaitForExit(8000) && runner.ExitCode == 0, "Actual node:test run");
                    Hook("PostToolUse", "Bash", "node --test runner.test.cjs", actual, "actual-pass");
                    Require(title.Text.Contains("通过"), "Real runner output: " + actual);
                }

                File.Delete(fixture);
                Hook("Stop", "", "", "", "after-pass");
                Require(title.Text.Contains("通过"), "Keep just-completed result visible");
                ((Button)win.FindName("EvoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(400);
                Require(Preferences.Load(out _).Mode == "evo", "Persistent mode");
                Require(copyEvo.Background == app.Resources["Accent"] && copyNeo.Background == app.Resources["Card"], "Evo primary action");
                Shot(win, args[0], "evo");
                var pref = new Preferences { Volume = 30 };
                using (var deck = new AudioDeck(pref))
                using (var endpoints = new MMDeviceEnumerator())
                {
                    Require(deck.Clips.Count == 8, "Eight cues");
                    foreach (var clip in deck.Clips.Values)
                    {
                        using (var decoded = VoiceOutput.Decode(AudioDeck.ReadBundled(clip), false, CancellationToken.None))
                            Require(decoded.Length > 0 && decoded.TotalTime.TotalSeconds <= 10.1, "Installed audio decodes: " + clip.Id);
                    }
                    byte[] wav = File.ReadAllBytes(Files.Asset("attention.wav"));
                    Require(AudioDeck.ValidWave(wav), "Valid PCM");
                    Require(!AudioDeck.ValidWave(wav.Take(40).ToArray()), "Truncated WAV");
                    var cue = deck.Clips["win"];
                    deck.Import(cue, Files.Asset("attention.wav"));
                    Require(deck.HasLocal(cue), "Import local");
                    string message = "";
                    deck.Notice += s => message = s;
                    bool measure = args.Contains("--audio-output");
                    if (measure)
                        Require(endpoints.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia), "A real render endpoint is required for this check");
                    if (measure)
                    {
                        using (var device = endpoints.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                        {
                            Task<bool> play = null;
                            float peak = MeasureOutput(() => play = deck.Play("win"), () => play.IsCompleted, device);
                            Require(Await(play), "Local WAV played through Windows: " + message);
                            Console.WriteLine("PASS imported WAV, render-session peak " + peak.ToString("0.0000"));
                        }
                    }
                    else if (!endpoints.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                        Require(!Await(deck.Play("win")) && message.Contains("输出设备"), "Missing endpoint must not report success: " + message);
                    using (var stream = VoiceOutput.Decode(wav, true, CancellationToken.None))
                        Require(stream.Length > 0 && stream.TotalTime > TimeSpan.Zero, "Offline PCM decode");
                    var interrupted = deck.Play("win");
                    deck.Stop();
                    Require(!Await(interrupted), "Stop cannot report completion");
                    File.WriteAllText(Path.Combine(SignalBus.Root, "voices", "win.wav"), "not a WAV");
                    Require(!Await(deck.Play("win")) && message.Contains("WAV"), "Corrupt local copy gives a visible error");
                    deck.RemoveLocal(cue);
                    Require(!deck.HasLocal(cue), "Remove local");
                    string original = Path.Combine(Files.Base, "assets", "voices", cue.Id + ".mp3");
                    byte[] bytes = File.ReadAllBytes(original);
                    try
                    {
                        File.Delete(original);
                        Require(!Await(deck.Play(cue.Id)) && message.Contains("找不到"), "Missing bundled recording is reported");
                        File.WriteAllBytes(original, new byte[] { 0, 1, 2, 3 });
                        Require(!Await(deck.Play(cue.Id)) && message.Contains("校验"), "Altered bundled recording is rejected");
                        deck.Import(cue, Files.Asset("attention.wav"));
                        if (measure)
                            Require(Await(deck.Play(cue.Id)), "Personal WAV takes precedence over the damaged bundled file");
                        deck.RemoveLocal(cue);
                    }
                    finally { File.WriteAllBytes(original, bytes); }

                    var nav = (StackPanel)win.FindName("Nav");
                    ((Button)nav.Children[1]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Shot(win, args[0], "evo-voices");
                    ((Button)win.FindName("NeoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Shot(win, args[0], "neo-voices");
                    Shot(win, args[0], "neo-compact", 610, 440);
                    if (measure)
                    {
                        deck.Notice += Console.WriteLine;
                        using (var device = endpoints.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                        {
                            var rows = (StackPanel)win.FindName("VoiceRows");
                            var stop = (Button)win.FindName("StopAudio");
                            var feedback = (TextBlock)win.FindName("VoiceFeedback");
                            var buttons = rows.Children.OfType<Border>()
                                .SelectMany(row => ((DockPanel)row.Child).Children.OfType<Button>()).ToList();
                            foreach (var clip in deck.Clips.Values)
                            {
                                var play = buttons.Single(b => AutomationProperties.GetName(b) == "播放 " + clip.Speaker + " " + clip.Line);
                                Require(AutomationProperties.GetHelpText(play).Contains(clip.Translation), "Accessible translation");
                                float peak = MeasureOutput(() => play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)), () => !stop.IsEnabled, device);
                                Require(feedback.Text == "已播完 · " + clip.Speaker + " · " + clip.Line, "Playback completion: " + clip.Id);
                                Console.WriteLine("PASS offline WPF play " + clip.Id + ", render-session peak " + peak.ToString("0.0000"));
                            }
                        }
                    }
                    Require(offline.Attempts == 0, "No HTTP requests during first-use playback");
                    Require(!Directory.Exists(Path.Combine(SignalBus.Root, "preview-cache")), "No runtime download cache");
                }

                if (args.Contains("--codex"))
                {
                    Require(!String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEX_HOME")), "Test requires an isolated Codex profile");
                    Await(PluginManager.Change(true));
                    Await(PluginManager.Change(true));
                    Require(File.Exists(Path.Combine(SignalBus.Root, "managed-marketplace.txt")), "CLI plugin registered");
                    Await(PluginManager.Change(false));
                    Await(PluginManager.Change(false));
                    Require(!File.Exists(Path.Combine(SignalBus.Root, "managed-marketplace.txt")), "CLI plugin removed");
                    Console.WriteLine("PASS Codex official CLI add, repeat add, remove and repeat remove");
                }

                foreach (var file in Directory.EnumerateFiles(SignalBus.Root, "*", SearchOption.AllDirectories))
                    Require(!File.ReadAllText(file).Contains("sensitive text"), "Private contents on disk");
                Console.WriteLine("PASS native WPF layout, 8 cues, named pipe, actual hook binary, task separation, privacy, persistence and local audio.");
                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.ToString());
                return 1;
            }
        }
    }
}

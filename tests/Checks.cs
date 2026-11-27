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

        static void Shot(MainWindow w, string root, string name)
        {
            var content = (FrameworkElement)w.Content;
            content.Measure(new Size(888, 655));
            content.Arrange(new Rect(0, 0, 888, 655));
            content.UpdateLayout();
            var img = new RenderTargetBitmap(888, 655, 96, 96, PixelFormats.Pbgra32);
            img.Render(content);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            using (var f = File.Create(Path.Combine(root, name + ".png")))
                enc.Save(f);
        }

        [STAThread]
        static int Main(string[] args)
        {
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
                Require(Preferences.Load(out _).Online == false, "Network default");
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
                var pref = new Preferences();
                using (var deck = new AudioDeck(pref))
                {
                    Require(deck.Clips.Count == 8, "Eight cues");
                    Require(!AudioDeck.Allowed("https://invalid.example/a.mp3"), "Host allowlist");
                    byte[] wav = File.ReadAllBytes(Files.Asset("attention.wav"));
                    Require(AudioDeck.ValidWave(wav), "Valid PCM");
                    Require(!AudioDeck.ValidWave(wav.Take(40).ToArray()), "Truncated WAV");
                    var cue = deck.Clips["win"];
                    deck.Import(cue, Files.Asset("attention.wav"));
                    Require(deck.HasLocal(cue), "Import local");
                    Require(deck.Play("win").Result, "Offline preview");
                    deck.RemoveLocal(cue);
                    Require(!deck.HasLocal(cue), "Remove local");
                    Require(!deck.Play("win").Result, "No unsolicited network");
                    if (args.Contains("--online"))
                    {
                        pref.Online = true;
                        deck.MutedPlayback = true;
                        deck.Notice += Console.WriteLine;
                        deck.PlaybackError += ex => Console.Error.WriteLine(ex.GetType().Name + " / " + ex.GetBaseException().GetType().Name + " / " + ex.GetBaseException().Message);
                        foreach (var clip in deck.Clips.Values)
                        {
                            Require(Await(deck.Play(clip.Id)), "Online verification failed: " + clip.Id);
                            var wait = Stopwatch.StartNew();
                            while (!deck.MediaDuration.HasTimeSpan && wait.ElapsedMilliseconds < 4500)
                                Pump(30);
                            Require(deck.MediaDuration.HasTimeSpan && deck.MediaDuration.TimeSpan.TotalSeconds > 0, "Media decoder: " + clip.Id);
                            Console.WriteLine("Verified source SHA-256 and Windows media duration: " + clip.Id + " " + deck.MediaDuration.TimeSpan.TotalSeconds.ToString("0.00") + "s");
                        }

                        deck.ClearOnline();
                        Require(!Directory.EnumerateFiles(Path.Combine(SignalBus.Root, "preview-cache")).Any(), "Cache cleared");
                    }
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

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;

namespace TwinShift
{
    public partial class App : Application
    {
        Mutex instance;
        Process badgeTarget;
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            try
            {
                instance = new Mutex(true, @"Local\TwinShift-" + SignalBus.Hash(SignalBus.Root.ToUpperInvariant()).Substring(0, 32), out bool first);
                if (Array.IndexOf(e.Args, "--setup-check") >= 0 || Array.IndexOf(e.Args, "--uninstall-check") >= 0)
                {
                    if (!first)
                    {
                        Shutdown(3);
                        return;
                    }

                    var marker = Path.Combine(SignalBus.Root, "managed-marketplace.txt");
                    if (Array.IndexOf(e.Args, "--uninstall-check") >= 0 && File.Exists(marker) && String.Equals(File.ReadAllText(marker), Path.Combine(Files.Base, "marketplace"), StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            System.Threading.Tasks.Task.Run(() => PluginManager.Change(false)).GetAwaiter().GetResult();
                        }
                        catch
                        {
                            Shutdown(8);
                            return;
                        }
                    }

                    Shutdown(0);
                    return;
                }

                if (!first)
                {
                    if (Array.IndexOf(e.Args, "--window-pid") < 0 && Array.IndexOf(e.Args, "--tray") < 0)
                        SignalBus.Send("show", Guid.NewGuid().ToString("N"), "ui");
                    Shutdown();
                    return;
                }

                int targetOption = Array.IndexOf(e.Args, "--window-pid");
                if (targetOption >= 0)
                {
                    if (targetOption + 1 >= e.Args.Length || !int.TryParse(e.Args[targetOption + 1], out int pid) || pid <= 0)
                        throw new ArgumentException("Invalid Codex window PID");
                    badgeTarget = Process.GetProcessById(pid);
                    if (!CodexDesktop.Matches(badgeTarget) || badgeTarget.MainWindowHandle == IntPtr.Zero || badgeTarget.HasExited)
                        throw new ArgumentException("Expected a running Codex desktop window");
                }
                Directory.CreateDirectory(SignalBus.Root);
                var window = new MainWindow(badgeTarget);
                MainWindow = window;
                if (Array.IndexOf(e.Args, "--tray") < 0)
                    window.Show();
            }
            catch (Exception err)
            {
                MessageBox.Show("Vedal AI 无法启动。请确认安装完整，且个人设置目录可以写入。\n" + err.GetType().Name, "Twin Shift", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            badgeTarget?.Dispose();
            instance?.Dispose();
            base.OnExit(e);
        }
    }
}

using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace TwinShift
{
    public partial class App : Application
    {
        Mutex instance;
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
                    SignalBus.Send("show", Guid.NewGuid().ToString("N"), "ui");
                    Shutdown();
                    return;
                }

                Directory.CreateDirectory(SignalBus.Root);
                var window = new MainWindow();
                MainWindow = window;
                window.Show();
            }
            catch (Exception err)
            {
                MessageBox.Show("值班室无法启动。请确认安装完整，且个人设置目录可以写入。\n" + err.GetType().Name, "Twin Shift", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            instance?.Dispose();
            base.OnExit(e);
        }
    }
}

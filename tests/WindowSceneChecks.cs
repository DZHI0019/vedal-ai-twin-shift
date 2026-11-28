using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace TwinShift
{
    internal static class WindowSceneChecks
    {
        [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint relation);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint relation);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point pt);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref Point pt);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out BadgeWindow.Rect rect);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);

        internal static int RunHost()
        {
            var fixture = new Window {
                Title = "Twin Shift · scene test", Width = 660, Height = 400, WindowStyle = WindowStyle.None,
                WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowActivated = false, ShowInTaskbar = false,
                Topmost = true, Background = new SolidColorBrush(Color.FromRgb(10, 10, 10))
            };
            fixture.Content = new System.Windows.Controls.Border { Background = fixture.Background };
            fixture.Loaded += (s, e) => {
                Console.WriteLine(new WindowInteropHelper(fixture).Handle.ToInt64());
                Console.Out.Flush();
                Task.Run(() => {
                    if (Console.ReadLine() == "close") fixture.Dispatcher.BeginInvoke(new Action(fixture.Close));
                });
            };
            var expiry = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
            expiry.Tick += (s, e) => fixture.Close();
            expiry.Start();
            return new Application().Run(fixture);
        }

        internal static void Paint()
        {
            var until = Stopwatch.StartNew();
            while (until.ElapsedMilliseconds < 360)
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Thread.Sleep(12);
            }
        }

        // Only sample the opaque test window belonging to the child process we launched.
        internal static uint[] Sample(IntPtr parent, IntPtr layer)
        {
            GetClientRect(parent, out var size);
            var output = new uint[9];
            var dc = GetDC(IntPtr.Zero);
            try
            {
                for (int i = 0; i < output.Length; i++)
                {
                    var pt = new Point { X = (int)(size.Right * (.76 + i % 3 * .06)), Y = (int)(size.Bottom * (.31 + i / 3 * .07)) };
                    ClientToScreen(parent, ref pt);
                    var hit = WindowFromPoint(pt);
                    if (hit == layer || GetAncestor(hit, 2) != parent) throw new Exception("Scene covered the input target or the test surface is occluded.");
                    uint pixel = GetPixel(dc, pt.X, pt.Y);
                    if (pixel == uint.MaxValue) throw new Exception("Cannot sample test desktop.");
                    output[i] = pixel;
                }
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }
            return output;
        }

        internal static void Run()
        {
            using (var fixture = Process.Start(new ProcessStartInfo(typeof(WindowSceneChecks).Assembly.Location, "--scene-host") {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardInput = true
            }))
            using (var scene = new WindowScene())
            {
                try
                {
                    var read = fixture.StandardOutput.ReadLineAsync();
                    if (!read.Wait(10000) || !long.TryParse(read.Result, out long value)) throw new Exception("Test window did not open.");
                    var parent = new IntPtr(value);
                    Paint();
                    var baseline = Sample(parent, IntPtr.Zero);
                    if (baseline.Any(x => x != 0x0a0a0a)) throw new Exception("Unexpected test window background.");
                    scene.Present(parent, true, true);
                    Paint();
                    if (GetWindow(scene.Handle, 4) != parent) throw new Exception("The test window does not own the scene.");
                    var dark = Sample(parent, scene.Handle);
                    if (dark.Distinct().Count() < 4 || dark.SequenceEqual(baseline)) throw new Exception("Evil artwork not painted on desktop.");
                    scene.Present(parent, false, true);
                    Paint();
                    var light = Sample(parent, scene.Handle);
                    if (light.Distinct().Count() < 4 || light.SequenceEqual(dark)) throw new Exception("neuro artwork did not replace Evil.");
                    var child = scene.Handle;
                    scene.Dispose();
                    Paint();
                    if (IsWindow(child) || !Sample(parent, IntPtr.Zero).SequenceEqual(baseline)) throw new Exception("Wallpaper remained after disabling.");
                    if (!WindowScene.Placement(420, 400).IsEmpty || WindowScene.Placement(1000, 600).Bottom > 490)
                        throw new Exception("Scene overlaps the compact input area.");
                    scene.Present(parent, true, true);
                    fixture.StandardInput.WriteLine("close");
                    var timeout = Stopwatch.StartNew();
                    while (!fixture.HasExited && timeout.ElapsedMilliseconds < 4500) Paint();
                    if (!fixture.HasExited) throw new Exception("The test window did not finish closing.");
                    if (scene.Parent != IntPtr.Zero) throw new Exception("Scene still reported a destroyed parent.");
                    Console.WriteLine("PASS cross-process wallpaper, both paintings, hit testing and restoration");
                }
                finally { if (!fixture.HasExited) fixture.StandardInput.WriteLine("close"); }
            }
        }
    }
}

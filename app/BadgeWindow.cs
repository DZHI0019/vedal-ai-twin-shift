using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TwinShift
{
    internal sealed class BadgeWindow : Window
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        static extern IntPtr GetWindow(IntPtr window, uint relation);
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr h, out Rect r);
        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr h, StringBuilder buffer, int length);
        [DllImport("user32.dll")]
        static extern uint GetDpiForWindow(IntPtr h);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        static extern int SetWindowLong(IntPtr h, int i, int value);
        readonly DispatcherTimer tracker = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(380)
        };
        readonly DispatcherTimer reset = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        readonly Preferences prefs;
        readonly Process target;
        readonly Border frame;
        readonly Glyph chip;
        readonly WindowScene scene = new WindowScene();
        readonly PreviewPanel preview = new PreviewPanel();
        string sceneError;
        internal event Action<string> SceneFailed;
        IntPtr cachedHandle;
        uint cachedPid;
        bool eligible;
        bool checkingPreview;
        internal BadgeWindow(Preferences prefs, Process target, Action click, Action panel)
        {
            this.prefs = prefs;
            this.target = target;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            var image = new Image
            {
                Width = 28,
                Height = 28,
                Source = Files.Image("tutel.png")
            };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            var strip = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            strip.Children.Add(image);
            chip = new Glyph
            {
                Width = 14,
                Height = 14,
                Margin = new Thickness(3, 0, 1, 0)
            };
            strip.Children.Add(chip);
            frame = new Border
            {
                Child = strip,
                Padding = new Thickness(5, 1, 4, 1),
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(1)
            };
            frame.SetResourceReference(Border.BackgroundProperty, "Card");
            frame.SetResourceReference(Border.BorderBrushProperty, "Edge");
            Content = frame;
            ToolTip = "Vedal AI · 点击听小龟，右键打开控制台";
            MouseLeftButtonUp += (s, e) => click();
            MouseRightButtonUp += (s, e) => panel();
            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x08000000 | 0x00000080);
            };
            tracker.Tick += (s, e) => Track();
            reset.Tick += (s, e) =>
            {
                reset.Stop();
                scene.Pulse(null);
                chip.Kind = prefs.Night ? "pipes" : "heart";
            };
        }

        internal void Apply()
        {
            reset.Stop();
            chip.Kind = prefs.Night ? "pipes" : "heart";
            if (!prefs.WindowScene) scene.Dispose();
            if ((prefs.WindowScene || prefs.Badge) && !SystemParameters.HighContrast)
            {
                tracker.Start();
                Track();
            }
            else
            {
                tracker.Stop();
                scene.Dispose();
                Hide();
            }
        }

        internal void Pulse(string glyph)
        {
            if (prefs.WindowScene && scene.Parent != IntPtr.Zero)
            {
                scene.Pulse(glyph);
                reset.Stop();
                reset.Start();
                return;
            }
            if (!IsVisible)
                return;
            chip.Kind = glyph;
            reset.Stop();
            reset.Start();
            if (prefs.QuietMotion || !SystemParameters.ClientAreaAnimation)
                return;
            chip.BeginAnimation(OpacityProperty, new DoubleAnimation(.35, 1, TimeSpan.FromMilliseconds(300)) { FillBehavior = FillBehavior.Stop });
        }

        async void Track()
        {
            try
            {
                IntPtr main = GetForegroundWindow();
                GetWindowThreadProcessId(main, out uint pid);
                if (target != null && target.HasExited)
                {
                    scene.Dispose();
                    tracker.Stop();
                    Hide();
                    return;
                }
                if (main != cachedHandle || pid != cachedPid)
                {
                    cachedHandle = main;
                    cachedPid = pid;
                    eligible = false;
                    var cls = new StringBuilder(100);
                    if ((target == null || pid == target.Id) && main != IntPtr.Zero && GetWindow(main, 4) == IntPtr.Zero &&
                        GetClassName(main, cls, 100) > 0 && cls.ToString() == "Chrome_WidgetWin_1")
                    {
                        using (var p = Process.GetProcessById((int)pid))
                            eligible = CodexDesktop.Matches(p);
                    }
                }

                if (prefs.WindowScene)
                {
                    if (IsVisible) Hide();
                    if (!eligible)
                    {
                        scene.Hide();
                        return;
                    }
                    if (scene.Parent != main) scene.Hide();
                    if (checkingPreview) return;
                    checkingPreview = true;
                    bool allowed;
                    try { allowed = await Task.Run(() => preview.AllowsArtwork(main)); }
                    finally { checkingPreview = false; }
                    if (!tracker.IsEnabled || !prefs.WindowScene || GetForegroundWindow() != main) return;
                    scene.Present(main, WindowScene.IsNight(main, prefs), prefs.Badge, allowed);
                    sceneError = null;
                    return;
                }
                if (!eligible || !GetWindowRect(main, out var r))
                {
                    Hide();
                    return;
                }

                double dpi = Math.Max(96u, GetDpiForWindow(main)) / 96.0;
                if (r.Right - r.Left < 940 * dpi || r.Bottom - r.Top < 490 * dpi)
                {
                    Hide();
                    return;
                }

                if (!IsVisible)
                    Show();
                SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), r.Left + (int)(15 * dpi), r.Bottom - (int)(78 * dpi), 0, 0, 0x0011);
            }
            catch (Exception e)when (e is ArgumentException || e is InvalidOperationException || e is System.ComponentModel.Win32Exception || e is System.IO.IOException)
            {
                scene.Dispose();
                if (prefs.WindowScene && sceneError != e.Message)
                {
                    sceneError = e.Message;
                    SceneFailed?.Invoke("主窗口场景没有加载。" + e.Message);
                }
                Hide();
            }
        }

        internal void Finish()
        {
            tracker.Stop();
            reset.Stop();
            scene.Dispose();
            Close();
        }
    }
}

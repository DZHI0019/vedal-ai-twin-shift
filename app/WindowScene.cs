using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TwinShift
{
    internal sealed class WindowScene : IDisposable
    {
        const int Popup = unchecked((int)0x80000000), Disabled = 0x08000000;
        const int Layered = 0x80000, Transparent = 0x20, ToolWindow = 0x80, NoActivate = 0x08000000;
        const uint NoZOrder = 0x0004, NoActivation = 0x0010, Show = 0x0040, NoOwnerZOrder = 0x0200;
        const uint LocationChange = 0x800b;
        delegate void WinEvent(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time);
        [StructLayout(LayoutKind.Sequential)] struct Origin { public int X, Y; }
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out BadgeWindow.Rect rect);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref Origin point);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool SetWindowText(IntPtr window, string title);
        [DllImport("dwmapi.dll")]
        static extern int DwmGetWindowAttribute(IntPtr window, int setting, out int value, int size);

        readonly Grid art = new Grid { IsHitTestVisible = false, Focusable = false, ClipToBounds = true };
        readonly ImageBrush picture = new ImageBrush { Stretch = Stretch.UniformToFill, AlignmentX = AlignmentX.Right };
        readonly Image turtle = new Image { Width = 27, Height = 27, Margin = new Thickness(0, 0, 6, 0) };
        readonly TextBlock signature = new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        readonly Glyph cue = new Glyph { Width = 14, Height = 14, Margin = new Thickness(6, 0, 0, 0), Visibility = Visibility.Collapsed };
        readonly Border sceneImage;
        readonly WinEvent locationChanged;
        HwndSource source;
        IntPtr host, locationHook;
        Rect previous = Rect.Empty;
        bool? shownNight;
        BitmapSource light, dark;

        internal WindowScene()
        {
            locationChanged = OnLocationChanged;
            var lateral = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
            lateral.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            lateral.GradientStops.Add(new GradientStop(Color.FromArgb(18, 0, 0, 0), .28));
            lateral.GradientStops.Add(new GradientStop(Color.FromArgb(58, 0, 0, 0), .53));
            lateral.GradientStops.Add(new GradientStop(Color.FromArgb(153, 0, 0, 0), .81));
            lateral.GradientStops.Add(new GradientStop(Color.FromArgb(144, 0, 0, 0), 1));
            lateral.Freeze();
            var vertical = new LinearGradientBrush { StartPoint = new Point(.5, 0), EndPoint = new Point(.5, 1) };
            vertical.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            vertical.GradientStops.Add(new GradientStop(Colors.Black, .17));
            vertical.GradientStops.Add(new GradientStop(Colors.Black, .76));
            vertical.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            vertical.Freeze();
            sceneImage = new Border { Background = picture, OpacityMask = lateral };
            art.Children.Add(new Border { Child = sceneImage, OpacityMask = vertical });
            RenderOptions.SetBitmapScalingMode(sceneImage, BitmapScalingMode.HighQuality);
            RenderOptions.SetBitmapScalingMode(turtle, BitmapScalingMode.NearestNeighbor);
            var imprint = new StackPanel {
                Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 18, 14)
            };
            imprint.Children.Add(turtle);
            imprint.Children.Add(signature);
            imprint.Children.Add(cue);
            art.Children.Add(imprint);
        }

        internal IntPtr Parent => source != null && !source.IsDisposed && IsWindow(host) ? host : IntPtr.Zero;
        internal IntPtr Handle => source == null || source.IsDisposed ? IntPtr.Zero : source.Handle;

        internal static Rect Placement(double width, double height)
        {
            if (width < 460 || height < 330) return Rect.Empty;
            double left = Math.Max(125, width * .25);
            double top = Math.Max(54, Math.Min(96, height * .105));
            double bottom = Math.Max(110, Math.Min(195, height * .23));
            double available = height - top - bottom;
            return new Rect(left, top, width - left, Math.Min(available, (width - left) * .69));
        }

        internal static bool IsNight(IntPtr window, Preferences preferences)
        {
            if (preferences.Mode == "system" && DwmGetWindowAttribute(window, 20, out int flag, sizeof(int)) == 0)
                return flag != 0;
            return preferences.Night;
        }

        static Rect ScreenPlacement(IntPtr parent)
        {
            var origin = new Origin();
            if (IsIconic(parent) || !GetClientRect(parent, out var size) || !ClientToScreen(parent, ref origin)) return Rect.Empty;
            double scale = Math.Max(96u, GetDpiForWindow(parent)) / 96d;
            var dip = Placement(size.Right / scale, size.Bottom / scale);
            if (dip.IsEmpty) return Rect.Empty;
            return new Rect(origin.X + Math.Round(dip.X * scale), origin.Y + Math.Round(dip.Y * scale),
                Math.Round(dip.Width * scale), Math.Round(dip.Height * scale));
        }

        internal void Present(IntPtr parent, bool night, bool showTurtle)
        {
            if (!IsWindow(parent)) { Dispose(); return; }
            var pixels = ScreenPlacement(parent);
            if (pixels.IsEmpty) { Hide(); return; }
            if (Parent != parent)
            {
                Dispose();
                var parameters = new HwndSourceParameters("Vedal AI") {
                    ParentWindow = parent, WindowStyle = Popup | Disabled,
                    ExtendedWindowStyle = Layered | Transparent | NoActivate | ToolWindow,
                    UsesPerPixelTransparency = true, PositionX = (int)pixels.X, PositionY = (int)pixels.Y,
                    Width = (int)pixels.Width, Height = (int)pixels.Height
                };
                source = new HwndSource(parameters) { SizeToContent = SizeToContent.Manual };
                host = parent;
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
                source.RootVisual = art;
                uint thread = GetWindowThreadProcessId(parent, out uint process);
                locationHook = SetWinEventHook(LocationChange, LocationChange, IntPtr.Zero, locationChanged, process, thread, 0);
            }
            if (shownNight != night)
            {
                var name = night ? "Evil" : "neuro";
                picture.ImageSource = night ? (dark ??= Files.Image("evo.jpg")) : (light ??= Files.Image("neo.jpg"));
                signature.Text = "Vedal AI · " + name;
                signature.Foreground = night ? Brushes.Wheat : Brushes.DarkSlateGray;
                cue.Stroke = signature.Foreground;
                sceneImage.Opacity = night ? 1 : .79;
                SetWindowText(source.Handle, signature.Text);
                shownNight = night;
            }
            if (showTurtle && turtle.Source == null) turtle.Source = Files.Image("tutel.png");
            turtle.Visibility = showTurtle ? Visibility.Visible : Visibility.Collapsed;
            if (pixels != previous && !Place(pixels)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        void OnLocationChanged(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time)
        {
            if (window != host || objectId != 0 || childId != 0 || previous.IsEmpty || Handle == IntPtr.Zero) return;
            var pixels = ScreenPlacement(host);
            if (pixels.IsEmpty) Hide();
            else if (pixels != previous) Place(pixels);
        }

        bool Place(Rect pixels)
        {
            if (!SetWindowPos(Handle, IntPtr.Zero, (int)pixels.X, (int)pixels.Y,
                (int)pixels.Width, (int)pixels.Height, NoActivation | NoOwnerZOrder | NoZOrder | Show)) return false;
            previous = pixels;
            return true;
        }

        internal void Hide()
        {
            if (host != IntPtr.Zero && !IsWindow(host)) { Dispose(); return; }
            if (!previous.IsEmpty && Handle != IntPtr.Zero) ShowWindow(Handle, 0);
            previous = Rect.Empty;
        }

        internal void Pulse(string kind)
        {
            cue.Kind = kind ?? "heart";
            cue.Visibility = kind == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public void Dispose()
        {
            if (locationHook != IntPtr.Zero)
            {
                UnhookWinEvent(locationHook);
                locationHook = IntPtr.Zero;
            }
            if (source != null && !source.IsDisposed)
            {
                source.RootVisual = null;
                source.Dispose();
            }
            source = null;
            host = IntPtr.Zero;
            previous = Rect.Empty;
            shownNight = null;
            Pulse(null);
        }
    }
}

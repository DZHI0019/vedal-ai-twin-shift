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
        internal const int Child = 0x40000000, Disabled = 0x08000000, Transparent = 0x20, NoActivate = 0x08000000;
        const int Visible = 0x10000000, Layered = 0x80000;
        const uint NoActivation = 0x0010;
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out BadgeWindow.Rect rect);
        [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
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
        HwndSource source;
        IntPtr host;
        Rect previous = Rect.Empty;
        bool? shownNight;
        BitmapSource light, dark;

        internal WindowScene()
        {
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

        internal void Present(IntPtr parent, bool night, bool showTurtle)
        {
            if (!IsWindow(parent)) { Dispose(); return; }
            if (IsIconic(parent)) return;
            if (!GetClientRect(parent, out var size)) { Dispose(); return; }
            double scale = Math.Max(96u, GetDpiForWindow(parent)) / 96d;
            var dip = Placement(size.Right / scale, size.Bottom / scale);
            if (dip.IsEmpty) { Dispose(); return; }
            var pixels = new Rect(Math.Round(dip.X * scale), Math.Round(dip.Y * scale),
                Math.Round(dip.Width * scale), Math.Round(dip.Height * scale));
            if (Parent != parent)
            {
                Dispose();
                var parameters = new HwndSourceParameters("Vedal AI") {
                    ParentWindow = parent, WindowStyle = Child | Visible | Disabled,
                    ExtendedWindowStyle = Layered | Transparent | NoActivate,
                    UsesPerPixelTransparency = true, PositionX = (int)pixels.X, PositionY = (int)pixels.Y,
                    Width = (int)pixels.Width, Height = (int)pixels.Height
                };
                source = new HwndSource(parameters) { SizeToContent = SizeToContent.Manual };
                host = parent;
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
                source.RootVisual = art;
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
            if (pixels != previous)
            {
                if (!SetWindowPos(source.Handle, IntPtr.Zero, (int)pixels.X, (int)pixels.Y,
                    (int)pixels.Width, (int)pixels.Height, NoActivation))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                previous = pixels;
            }
        }

        internal void Pulse(string kind)
        {
            cue.Kind = kind ?? "heart";
            cue.Visibility = kind == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public void Dispose()
        {
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

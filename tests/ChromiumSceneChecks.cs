using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace TwinShift
{
    internal static class ChromiumSceneChecks
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out BadgeWindow.Rect rect);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint message, IntPtr wp, IntPtr lp);

        internal static void Run(string browser, string scratch)
        {
            const string title = "Twin Shift Chromium paint fixture";
            string folder = Directory.CreateDirectory(Path.Combine(scratch, "chromium-" + Guid.NewGuid().ToString("N"))).FullName;
            string html = Path.Combine(folder, "fixture.html");
            File.WriteAllText(html, "<!doctype html><meta charset=utf-8><title>" + title +
                "</title><style>html,body{background:#0a0a0a;color:#ddd}body{font:16px system-ui}span{display:block;width:3px;height:3px;background:#ddd;animation:p 1s infinite alternate}@keyframes p{to{transform:translateX(20px)}}</style><p>" + title + "</p><span></span>");
            string args = "--app=\"" + new Uri(html).AbsoluteUri + "\" --user-data-dir=\"" + Path.Combine(folder, "profile") +
                "\" --no-first-run --no-default-browser-check --disable-sync --disable-default-apps --disable-background-mode --disable-background-networking --window-size=920,590";
            using (var process = Process.Start(new ProcessStartInfo(browser, args) { UseShellExecute = false }))
            using (var scene = new WindowScene())
            {
                IntPtr parent = IntPtr.Zero;
                try
                {
                    var wait = Stopwatch.StartNew();
                    while (wait.Elapsed < TimeSpan.FromSeconds(16))
                    {
                        if (process.HasExited) throw new Exception("The isolated Chromium process exited.");
                        process.Refresh();
                        var candidate = process.MainWindowHandle;
                        var caption = new StringBuilder(180);
                        GetWindowText(candidate, caption, caption.Capacity);
                        if (caption.ToString().Contains(title)) { parent = candidate; break; }
                        Thread.Sleep(80);
                    }
                    if (parent == IntPtr.Zero) throw new Exception("The local fixture page did not open.");
                    const uint noMoveOrFocus = 0x0013;
                    SetWindowPos(parent, IntPtr.Zero, 0, 0, 0, 0, noMoveOrFocus);
                    IntPtr renderer, gpu;
                    wait.Restart();
                    do {
                        WindowSceneChecks.Paint();
                        renderer = FindWindowEx(parent, IntPtr.Zero, "Chrome_RenderWidgetHostHWND", null);
                        gpu = FindWindowEx(parent, IntPtr.Zero, "Intermediate D3D Window", null);
                    } while ((renderer == IntPtr.Zero || gpu == IntPtr.Zero) && wait.Elapsed < TimeSpan.FromSeconds(5));
                    if (renderer == IntPtr.Zero || gpu == IntPtr.Zero)
                    {
                        var classes = new StringBuilder();
                        for (var child = FindWindowEx(parent, IntPtr.Zero, null, null); child != IntPtr.Zero; child = FindWindowEx(parent, child, null, null))
                        {
                            var cls = new StringBuilder(100);
                            GetClassName(child, cls, cls.Capacity);
                            classes.Append(" ").Append(cls);
                        }
                        throw new Exception("Expected a Chromium rendering widget and D3D surface. Found:" + classes);
                    }
                    WindowSceneChecks.Paint();
                    var baseline = WindowSceneChecks.Sample(parent, IntPtr.Zero);
                    if (baseline.Any(p => p != 0x0a0a0a)) throw new Exception("The fixture background was not exposed.");
                    scene.Present(parent, true, true);
                    WindowSceneChecks.Paint();
                    if ((GetWindowLong(scene.Handle, -20) & 8) != 0) throw new Exception("The scene covers unrelated applications.");
                    SetWindowPos(renderer, IntPtr.Zero, 0, 0, 0, 0, noMoveOrFocus);
                    WindowSceneChecks.Paint();
                    var dark = WindowSceneChecks.Sample(parent, scene.Handle);
                    if (dark.Distinct().Count() < 4 || dark.SequenceEqual(baseline)) throw new Exception("Evil is not visible over the Chromium renderer.");
                    GetWindowRect(parent, out var browserBounds);
                    GetWindowRect(scene.Handle, out var beforeMove);
                    const int dx = 32, dy = 16;
                    SetWindowPos(parent, IntPtr.Zero, browserBounds.Left + dx, browserBounds.Top + dy, 0, 0, 0x0015);
                    WindowSceneChecks.Paint();
                    GetWindowRect(scene.Handle, out var afterMove);
                    if (afterMove.Left != beforeMove.Left + dx || afterMove.Top != beforeMove.Top + dy)
                        throw new Exception("The scene did not follow the moving browser.");
                    scene.Hide();
                    WindowSceneChecks.Paint();
                    if (!WindowSceneChecks.Sample(parent, IntPtr.Zero).SequenceEqual(baseline)) throw new Exception("The background stayed visible after hiding.");
                    scene.Present(parent, false, true);
                    WindowSceneChecks.Paint();
                    var light = WindowSceneChecks.Sample(parent, scene.Handle);
                    if (light.Distinct().Count() < 4 || light.SequenceEqual(dark)) throw new Exception("neuro is not visible after switching scene.");
                    scene.Dispose();
                    WindowSceneChecks.Paint();
                    if (!WindowSceneChecks.Sample(parent, IntPtr.Zero).SequenceEqual(baseline)) throw new Exception("The Chromium surface was not restored.");
                    Console.WriteLine("PASS hardware-composited Chromium: both scenes, input hit testing, window movement, hiding and restoration.");
                }
                finally
                {
                    scene.Dispose();
                    if (parent != IntPtr.Zero) PostMessage(parent, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    else if (!process.HasExited) process.CloseMainWindow();
                }
            }
        }
    }
}

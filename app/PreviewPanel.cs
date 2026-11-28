using System;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace TwinShift
{
    internal sealed class PreviewPanel
    {
        // Chromium exposes the shell's numbered tab panels separately from the primary chat.
        // MatchSubstring is supported by Windows 10 1809+ but isn't named in the .NET 4.8 enum.
        const PropertyConditionFlags MatchSubstring = (PropertyConditionFlags)2;
        static readonly Condition Document = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document);
        static readonly Condition OpenTab = new AndCondition(
            new PropertyCondition(AutomationElement.AutomationIdProperty, "app-shell-tab-panel-app-shell-tab:", MatchSubstring),
            new PropertyCondition(AutomationElement.IsOffscreenProperty, false));
        IntPtr host;
        AutomationElement document;

        internal bool AllowsArtwork(IntPtr window)
        {
            if (host != window) { host = window; document = null; }
            try
            {
                document ??= AutomationElement.FromHandle(window)?.FindFirst(TreeScope.Descendants, Document);
                return document != null && document.FindFirst(TreeScope.Descendants, OpenTab) == null;
            }
            catch (Exception error) when (error is ElementNotAvailableException || error is COMException ||
                error is InvalidOperationException || error is UnauthorizedAccessException)
            {
                document = null;
                return false;
            }
        }
    }
}

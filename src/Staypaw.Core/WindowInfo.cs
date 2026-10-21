using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Security;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Staypaw.Core;

// Ported from NoFocusSteal's WindowInfo.cs at 279e9cd (MIT); see THIRD-PARTY-NOTICES.md.

/// <summary>A snapshot of a window and the process that owns it, taken the moment it took the foreground
/// (short-lived stealers are often gone a few milliseconds later).</summary>
internal sealed class WindowInfo
{
    // Parts of the Windows shell that rightly take focus when you open them.
    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "StartMenuExperienceHost.exe", "SearchHost.exe", "SearchApp.exe", "SearchUI.exe",
        "ShellExperienceHost.exe", "ShellHost.exe", "LockApp.exe", "LogonUI.exe", "consent.exe",
        "TextInputHost.exe", "ScreenClippingHost.exe", "CredentialUIBroker.exe",
    };

    private static readonly HashSet<string> SystemExplorerClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "TaskSwitcherWnd", "MultitaskingViewFrame",
        "XamlExplorerHostIslandWindow", "ForegroundStaging", "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland", "Windows.UI.Core.CoreWindow", "#32768",
    };

    // Helper windows that only host input-method UI. They should never have focus, but a Windows 11 24H2
    // regression hands focus to explorer.exe's one after clicks, knocking games out.
    private static readonly HashSet<string> BogusClasses = new(StringComparer.Ordinal) { "MSCTFIME UI", "IME" };

    public static readonly bool SelfElevated = IsElevated(PInvoke.GetCurrentProcess());

    public HWND Hwnd;
    public HWND RootOwner;
    public uint ProcessId;
    public string ExePath = "";
    public string ExeName = "";
    public string Title = "";
    public string ClassName = "";
    public bool Visible;
    public int? ProcessStartTick;

    /// <summary>The process runs as administrator or higher, so a normal process can't see input sent to it.</summary>
    public bool Elevated;

    public bool IsSystemUI =>
        SystemProcesses.Contains(ExeName)
        || (ExeName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) && SystemExplorerClasses.Contains(ClassName));

    public bool IsBogus => BogusClasses.Contains(ClassName);

    public static unsafe WindowInfo Capture(HWND hwnd)
    {
        var info = new WindowInfo
        {
            Hwnd = hwnd,
            RootOwner = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOTOWNER),
            Visible = PInvoke.IsWindowVisible(hwnd),
        };

        Span<char> text = stackalloc char[512];
        var length = PInvoke.GetWindowText(hwnd, text);
        info.Title = new string(text[..Math.Max(length, 0)]);
        length = PInvoke.GetClassName(hwnd, text);
        info.ClassName = new string(text[..Math.Max(length, 0)]);

        PInvoke.GetWindowThreadProcessId(hwnd, out info.ProcessId);
        var process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, info.ProcessId);
        if (!process.IsNull)
        {
            try
            {
                var size = (uint)text.Length;
                fixed (char* path = text)
                {
                    if (PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, path, &size))
                    {
                        info.ExePath = new string(path, 0, (int)size);
                        info.ExeName = Path.GetFileName(info.ExePath);
                    }
                }

                info.Elevated = IsElevated(process);
                info.ProcessStartTick = StartTick(process);
            }
            finally
            {
                PInvoke.CloseHandle(process);
            }
        }

        if (info.ExeName.Length == 0)
        {
            info.ExeName = info.ProcessId is 0 or 4 ? "System" : $"PID {info.ProcessId}";
        }

        return info;
    }

    /// <summary>True if the window no longer exists, is hidden, minimized or cloaked (e.g. on another virtual desktop).</summary>
    public static unsafe bool IsGone(HWND hwnd)
    {
        if (!PInvoke.IsWindow(hwnd) || !PInvoke.IsWindowVisible(hwnd) || PInvoke.IsIconic(hwnd))
        {
            return true;
        }

        var cloaked = 0;
        return PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(int)).Succeeded && cloaked != 0;
    }

    private static unsafe bool IsElevated(HANDLE process)
    {
        // A token we aren't allowed to query belongs to a more privileged process, which is what matters here.
        HANDLE token;
        if (!PInvoke.OpenProcessToken(process, TOKEN_ACCESS_MASK.TOKEN_QUERY, &token))
        {
            return true;
        }

        try
        {
            TOKEN_ELEVATION elevation;
            uint size;
            return PInvoke.GetTokenInformation(token, TOKEN_INFORMATION_CLASS.TokenElevation, &elevation, (uint)sizeof(TOKEN_ELEVATION), &size)
                && elevation.TokenIsElevated != 0;
        }
        finally
        {
            PInvoke.CloseHandle(token);
        }
    }

    private static unsafe int? StartTick(HANDLE process)
    {
        System.Runtime.InteropServices.ComTypes.FILETIME creation, exit, kernel, user;
        if (!PInvoke.GetProcessTimes(process, &creation, &exit, &kernel, &user))
        {
            return null;
        }

        var ticks = ((long)(uint)creation.dwHighDateTime << 32) | (uint)creation.dwLowDateTime;
        var ageMs = (DateTime.UtcNow - DateTime.FromFileTimeUtc(ticks)).TotalMilliseconds;
        return ticks > 0 && ageMs is >= 0 and < int.MaxValue / 2 ? unchecked(Environment.TickCount - (int)ageMs) : null;
    }
}

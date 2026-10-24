using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Staypaw.Core;

/// <summary>
/// A low-level keyboard hook (<c>WH_KEYBOARD_LL</c>) on a thread of its own. Windows calls it inside this process, so
/// nothing is injected anywhere. Every key on the system waits for its answer, and Windows quietly removes a hook
/// that answers too slowly, so it gets a thread that does nothing else: the UI thread can be busy drawing.
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private readonly Func<KBDLLHOOKSTRUCT, bool> _hold;
    private readonly HOOKPROC _proc;
    private readonly Thread _thread;
    private uint _threadId;

    /// <param name="hold">Returns true to keep the key from reaching any window.</param>
    public KeyboardHook(Func<KBDLLHOOKSTRUCT, bool> hold)
    {
        _hold = hold;
        _proc = OnKey;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Staypaw keyboard hook" };
        _thread.Start();
        ready.Wait();
    }

    public bool Installed { get; private set; }

    public void Dispose()
    {
        if (_threadId != 0)
        {
            PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, 0, 0);
        }

        _thread.Join();
    }

    private unsafe void Run(ManualResetEventSlim ready)
    {
        using var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _proc, PInvoke.GetModuleHandle((string?)null), 0);
        Installed = !hook.IsInvalid;
        _threadId = PInvoke.GetCurrentThreadId();
        ready.Set();
        if (!Installed)
        {
            Trace.WriteLine("Staypaw: keyboard hook failed: " + System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
            return;
        }

        // Windows calls the hook from inside GetMessage; no message ever arrives but WM_QUIT.
        MSG msg;
        while (PInvoke.GetMessage(&msg, HWND.Null, 0, 0) > 0)
        {
        }
    }

    private unsafe LRESULT OnKey(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0)
        {
            try
            {
                if (_hold(*(KBDLLHOOKSTRUCT*)lParam.Value))
                {
                    return new LRESULT(1);
                }
            }
            catch (Exception ex)
            {
                // A bug must never take the keyboard: let the key through.
                Trace.WriteLine("Staypaw: keyboard hook failed: " + ex);
            }
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }
}

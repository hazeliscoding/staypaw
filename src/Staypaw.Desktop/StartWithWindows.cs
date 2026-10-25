using System.Diagnostics;
using System.Security;
using Microsoft.Win32;

namespace Staypaw.Desktop;

// Brings protection back after a restart, and after a winget upgrade, which stops Staypaw and doesn't start it again.
public static class StartWithWindows
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Staypaw";

    private static readonly string Command = $"\"{Environment.ProcessPath}\"";

    // On means the entry starts this exe, so a portable copy and an installed one never remove each other's.
    public static bool IsOn(string runKey = RunKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(runKey);
        return Command.Equals(key?.GetValue(ValueName) as string, StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(bool on, string runKey = RunKey)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(runKey);
            if (on)
            {
                key.SetValue(ValueName, Command);
            }
            else if (IsOn(runKey))
            {
                key.DeleteValue(ValueName);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Trace.WriteLine("Staypaw: couldn't change starting with Windows: " + ex.Message);
        }
    }
}

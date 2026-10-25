using Microsoft.Win32;
using Staypaw.Desktop;

namespace Staypaw.Tests;

// Uses a key of its own, so the tests never touch the real Run key.
public sealed class StartWithWindowsTests : IDisposable
{
    private const string TestKey = @"Software\Staypaw.Tests\Run";

    [Fact]
    public void Turning_it_on_then_off_adds_and_removes_this_exe()
    {
        StartWithWindows.Set(true, TestKey);

        Assert.True(StartWithWindows.IsOn(TestKey));
        using (var key = Registry.CurrentUser.OpenSubKey(TestKey)!)
        {
            Assert.Equal($"\"{Environment.ProcessPath}\"", key.GetValue("Staypaw"));
        }

        StartWithWindows.Set(false, TestKey);

        Assert.False(StartWithWindows.IsOn(TestKey));
        using (var key = Registry.CurrentUser.OpenSubKey(TestKey)!)
        {
            Assert.Null(key.GetValue("Staypaw"));
        }
    }

    // The uninstall hook turns it off, which mustn't remove a portable copy's entry.
    [Fact]
    public void Turning_it_off_leaves_another_copys_entry_alone()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(TestKey))
        {
            key.SetValue("Staypaw", @"""D:\Portable\Staypaw\Staypaw.exe""");
        }

        Assert.False(StartWithWindows.IsOn(TestKey));
        StartWithWindows.Set(false, TestKey);

        using (var key = Registry.CurrentUser.OpenSubKey(TestKey)!)
        {
            Assert.Equal(@"""D:\Portable\Staypaw\Staypaw.exe""", key.GetValue("Staypaw"));
        }
    }

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\Staypaw.Tests", throwOnMissingSubKey: false);
}

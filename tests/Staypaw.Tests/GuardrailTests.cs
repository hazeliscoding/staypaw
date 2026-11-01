using System.Reflection;
using System.Text.RegularExpressions;

namespace Staypaw.Tests;

/// <summary>Structural rules from AGENTS.md: the core has no UI and no updater, Staypaw's own code uses no network (the
/// update check is Velopack's), every hook stays in Staypaw's own process, and other processes are only asked who they are.</summary>
public class GuardrailTests
{
    private static readonly Assembly Core = Assembly.Load("Staypaw.Core");
    private static readonly Assembly Desktop = Assembly.Load("Staypaw");

    // Only the low-level hooks run in the process that sets them; every other system-wide hook loads into other apps.
    [Fact]
    public void Every_Windows_hook_is_low_level()
    {
        var hooks = HookTypes(Sources());

        Assert.NotEmpty(hooks);
        Assert.All(hooks, hook => Assert.EndsWith("_LL", hook, StringComparison.Ordinal));
    }

    [Fact]
    public void The_check_finds_a_hook_that_loads_into_other_apps() =>
        Assert.Equal(["WH_KEYBOARD"], HookTypes(["PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD, proc, module, 0);"]));

    // A game's process may be one of them, and its anti-cheat watches for handles that can read or drive it.
    [Fact]
    public void Every_process_is_opened_only_to_ask_who_it_is()
    {
        var rights = OpenProcessRights(Sources());

        Assert.NotEmpty(rights);
        Assert.All(rights, access => Assert.Equal("PROCESS_QUERY_LIMITED_INFORMATION", access));
    }

    [Fact]
    public void The_check_finds_a_process_opened_to_read_its_memory() =>
        Assert.Equal(["PROCESS_QUERY_LIMITED_INFORMATION|PROCESS_VM_READ"],
            OpenProcessRights(["PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ, false, pid);"]));

    [Fact]
    public void Core_does_not_reference_Avalonia() => Assert.Empty(References(Core, "Avalonia"));

    [Fact]
    public void Staypaws_own_code_uses_no_network()
    {
        Assert.Empty(References(Core, "System.Net"));
        Assert.Empty(References(Desktop, "System.Net"));
    }

    // Oopsling only writes crash reports to disk. The user sends one themselves, from their own browser or mail app.
    [Fact]
    public void Oopsling_uses_no_network()
    {
        Assert.Empty(References(Assembly.Load("Oopsling"), "System.Net"));
        Assert.Empty(References(Assembly.Load("Oopsling.Avalonia"), "System.Net"));
    }

    // The watcher, which sees your keys, never even links the code that talks to GitHub.
    [Fact]
    public void Core_does_not_reference_Velopack() => Assert.Empty(References(Core, "Velopack"));

    // Positive controls: the same check finds the references it exists to catch.
    [Fact]
    public void The_check_finds_Avalonia_in_the_tray_app() => Assert.NotEmpty(References(Desktop, "Avalonia"));

    [Fact]
    public void The_check_finds_Velopack_in_the_tray_app() => Assert.NotEmpty(References(Desktop, "Velopack"));

    [Fact]
    public void The_check_finds_network_references() =>
        Assert.NotEmpty(References(Assembly.Load("System.Net.Http"), "System.Net"));

    private static IEnumerable<string> Sources()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "AGENTS.md")))
        {
            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar))!;
        }

        // Build output holds CsWin32's generated declarations, which take any access as a parameter.
        return Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin"))
            .Select(File.ReadAllText);
    }

    private static string[] HookTypes(IEnumerable<string> sources) =>
        [.. sources.SelectMany(source => Regex.Matches(source, @"SetWindowsHookEx\(\s*(?:WINDOWS_HOOK_ID\.)?(\w+)"))
            .Select(match => match.Groups[1].Value)];

    private static string[] OpenProcessRights(IEnumerable<string> sources) =>
        [.. sources.SelectMany(source => Regex.Matches(source, @"\bOpenProcess(?:_SafeHandle)?\(\s*([^,]+),"))
            .Select(match => Regex.Replace(match.Groups[1].Value, @"PROCESS_ACCESS_RIGHTS\.|\s", ""))];

    private static string[] References(Assembly assembly, string prefix) =>
        [.. assembly.GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))];
}

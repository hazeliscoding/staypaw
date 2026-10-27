using System.Reflection;
using System.Text.RegularExpressions;

namespace Staypaw.Tests;

/// <summary>Structural rules from AGENTS.md: the core has no UI and no updater, Staypaw's own code uses no network (the
/// update check is Velopack's), and every hook stays in Staypaw's own process.</summary>
public class GuardrailTests
{
    private static readonly Assembly Core = Assembly.Load("Staypaw.Core");
    private static readonly Assembly Desktop = Assembly.Load("Staypaw");

    // Only the low-level hooks run in the process that sets them; every other system-wide hook loads into other apps.
    [Fact]
    public void Every_Windows_hook_is_low_level()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "AGENTS.md")))
        {
            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar))!;
        }

        var hooks = HookTypes(Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));

        Assert.NotEmpty(hooks);
        Assert.All(hooks, hook => Assert.EndsWith("_LL", hook, StringComparison.Ordinal));
    }

    [Fact]
    public void The_check_finds_a_hook_that_loads_into_other_apps() =>
        Assert.Equal(["WH_KEYBOARD"], HookTypes(["PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD, proc, module, 0);"]));

    [Fact]
    public void Core_does_not_reference_Avalonia() => Assert.Empty(References(Core, "Avalonia"));

    [Fact]
    public void Staypaws_own_code_uses_no_network()
    {
        Assert.Empty(References(Core, "System.Net"));
        Assert.Empty(References(Desktop, "System.Net"));
    }

    // Oopsling only writes crash reports to disk. The user files one themselves, from their own browser.
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

    private static string[] HookTypes(IEnumerable<string> sources) =>
        [.. sources.SelectMany(source => Regex.Matches(source, @"SetWindowsHookEx\(\s*(?:WINDOWS_HOOK_ID\.)?(\w+)"))
            .Select(match => match.Groups[1].Value)];

    private static string[] References(Assembly assembly, string prefix) =>
        [.. assembly.GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))];
}

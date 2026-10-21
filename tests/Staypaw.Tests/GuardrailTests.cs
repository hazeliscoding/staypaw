using System.Reflection;

namespace Staypaw.Tests;

/// <summary>Structural rules from AGENTS.md: the core has no UI, and nothing uses the network.</summary>
public class GuardrailTests
{
    private static readonly Assembly Core = Assembly.Load("Staypaw.Core");
    private static readonly Assembly Desktop = Assembly.Load("Staypaw");

    [Fact]
    public void Core_does_not_reference_Avalonia() => Assert.Empty(References(Core, "Avalonia"));

    [Fact]
    public void No_Staypaw_assembly_uses_the_network()
    {
        Assert.Empty(References(Core, "System.Net"));
        Assert.Empty(References(Desktop, "System.Net"));
    }

    // Positive controls: the same check finds the references it exists to catch.
    [Fact]
    public void The_check_finds_Avalonia_in_the_tray_app() => Assert.NotEmpty(References(Desktop, "Avalonia"));

    [Fact]
    public void The_check_finds_network_references() =>
        Assert.NotEmpty(References(Assembly.Load("System.Net.Http"), "System.Net"));

    private static string[] References(Assembly assembly, string prefix) =>
        [.. assembly.GetReferencedAssemblies()
            .Select(name => name.Name!)
            .Where(name => name.StartsWith(prefix, StringComparison.Ordinal))];
}

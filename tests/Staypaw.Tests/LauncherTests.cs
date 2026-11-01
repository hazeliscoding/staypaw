using System.Diagnostics;
using Staypaw.Core;

namespace Staypaw.Tests;

public class LauncherTests
{
    // A Playwright MCP server's browser: chrome.exe (40) under node.exe (30) under claude.exe (20) under explorer.exe (10).
    private static readonly Dictionary<uint, ProcessEntry> AgentBrowser = new()
    {
        [10] = new(1, "explorer.exe"),
        [20] = new(10, "claude.exe"),
        [30] = new(20, "node.exe"),
        [40] = new(30, "chrome.exe"),
    };

    // Each process started after its parent, as real ones do.
    private static long? InOrder(uint id) => id * 1000L;

    [Fact]
    public void A_browser_an_agent_started_has_the_agent_as_its_launcher() =>
        Assert.Equal("claude.exe", Launchers.Walk(40, InOrder(40)!.Value, AgentBrowser, InOrder));

    [Fact]
    public void Codex_is_a_launcher_whatever_the_case_of_its_name()
    {
        var processes = new Dictionary<uint, ProcessEntry> { [10] = new(1, "explorer.exe"), [20] = new(10, "Codex.exe"), [30] = new(20, "chrome.exe") };

        Assert.Equal("Codex.exe", Launchers.Walk(30, InOrder(30)!.Value, processes, InOrder));
    }

    [Fact]
    public void The_agents_own_window_has_no_launcher() =>
        Assert.Null(Launchers.Walk(20, InOrder(20)!.Value, AgentBrowser, InOrder));

    [Fact]
    public void A_browser_you_started_has_no_launcher()
    {
        var processes = new Dictionary<uint, ProcessEntry>(AgentBrowser) { [50] = new(10, "chrome.exe") };

        Assert.Null(Launchers.Walk(50, InOrder(50)!.Value, processes, InOrder));
    }

    [Fact]
    public void A_reused_process_ID_is_not_a_parent()
    {
        // claude.exe exited and a later process took its ID, after node.exe had started.
        static long? Reused(uint id) => id == 20 ? 35_000 : InOrder(id);

        Assert.Null(Launchers.Walk(40, InOrder(40)!.Value, AgentBrowser, Reused));
    }

    [Fact]
    public void The_walk_stops_at_a_parent_that_has_exited()
    {
        var processes = new Dictionary<uint, ProcessEntry>(AgentBrowser);
        processes.Remove(30);

        Assert.Null(Launchers.Walk(40, InOrder(40)!.Value, processes, InOrder));
    }

    [Fact]
    public void The_walk_stops_at_a_parent_it_cannot_open()
    {
        static long? Shielded(uint id) => id == 30 ? null : InOrder(id);

        Assert.Null(Launchers.Walk(40, InOrder(40)!.Value, AgentBrowser, Shielded));
    }

    [Fact]
    public void The_walk_climbs_10_parents_and_no_more()
    {
        // claude.exe is process 1, and each of processes 2 to 12 is node.exe started by the one before.
        var processes = new Dictionary<uint, ProcessEntry> { [1] = new(0, "claude.exe") };
        for (uint id = 2; id <= 12; id++)
        {
            processes[id] = new(id - 1, "node.exe");
        }

        Assert.Equal("claude.exe", Launchers.Walk(11, InOrder(11)!.Value, processes, InOrder));
        Assert.Null(Launchers.Walk(12, InOrder(12)!.Value, processes, InOrder));
    }

    [Fact]
    public void A_Playwright_browser_is_an_agent_window_on_its_own_path() =>
        Assert.Equal("Playwright", new Launchers().AgentOf(0, Path.Combine(LocalAppData, "ms-playwright", "chromium-1234", "chrome-win64", "chrome.exe")));

    [Fact]
    public void A_folder_that_only_starts_like_Playwright_s_does_not_count() =>
        Assert.Null(new Launchers().AgentOf(0, Path.Combine(LocalAppData, "ms-playwright-go", "chrome.exe")));

    [Fact]
    public void A_launcher_is_found_among_real_processes()
    {
        using var agent = FakeAgent.Start();

        Assert.Equal("claude.exe", new Launchers().Find((uint)agent.Child.Id));
    }

    [Fact]
    public void The_answer_is_kept_after_the_launcher_exits()
    {
        using var agent = FakeAgent.Start();
        var launchers = new Launchers();
        launchers.Find((uint)agent.Child.Id);

        agent.Launcher.Kill();
        agent.Launcher.WaitForExit();

        Assert.Equal("claude.exe", launchers.Find((uint)agent.Child.Id));
        Assert.Null(new Launchers().Find((uint)agent.Child.Id));
    }

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>A copy of cmd.exe named claude.exe, running ping so it has a child that can outlive it.</summary>
    private sealed class FakeAgent : IDisposable
    {
        private readonly DirectoryInfo _folder;

        private FakeAgent(DirectoryInfo folder, Process launcher, Process child) => (_folder, Launcher, Child) = (folder, launcher, child);

        public Process Launcher { get; }

        public Process Child { get; }

        public static FakeAgent Start()
        {
            var folder = Directory.CreateTempSubdirectory("staypaw-launcher-");
            var claude = Path.Combine(folder.FullName, "claude.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), claude);
            var launcher = Process.Start(new ProcessStartInfo(claude, "/c ping -n 30 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
            for (var i = 0; i < 50; i++)
            {
                if (Process.GetProcessesByName("PING").FirstOrDefault(p => p.StartTime >= launcher.StartTime) is { } child)
                {
                    return new FakeAgent(folder, launcher, child);
                }

                Thread.Sleep(100);
            }

            launcher.Kill();
            throw new TimeoutException("The fake agent never started ping.");
        }

        public void Dispose()
        {
            foreach (var process in new[] { Child, Launcher })
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit();
                }
            }

            _folder.Delete(recursive: true);
        }
    }
}

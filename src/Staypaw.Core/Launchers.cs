using Windows.Win32;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.Threading;

namespace Staypaw.Core;

/// <summary>A process as a Toolhelp snapshot lists it.</summary>
internal readonly record struct ProcessEntry(uint ParentId, string ExeName);

/// <summary>
/// Finds the coding agent behind a window from the processes that started it (ROADMAP.md, 2026-10-30). Only the
/// watcher's thread calls it.
/// </summary>
internal sealed class Launchers
{
    private const int MaxParents = 10;

    // Matched against the exe names a Toolhelp snapshot lists, so no process is opened to read its path.
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase) { "claude.exe", "codex.exe" };

    private static readonly string PlaywrightBrowsers =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ms-playwright") + Path.DirectorySeparatorChar;

    // Keyed by start time too, since Windows reuses the IDs of exited processes.
    // shortcut: entries for exited processes are never dropped, which is kilobytes at a few hundred apps a day; prune them if it ever shows in memory.
    private readonly Dictionary<(uint Id, long Start), string?> _known = [];

    /// <summary>
    /// The agent behind a window's process: "Playwright" for a browser Playwright installed, or else the first listed
    /// launcher among the processes that started it.
    /// </summary>
    public string? AgentOf(uint processId, string exePath) =>
        exePath.StartsWith(PlaywrightBrowsers, StringComparison.OrdinalIgnoreCase) ? "Playwright" : Find(processId);

    /// <summary>The first listed launcher among the processes that started this one, kept for the life of the process.</summary>
    public string? Find(uint processId)
    {
        if (CreationTime(processId) is not { } start)
        {
            return null;
        }

        if (!_known.TryGetValue((processId, start), out var launcher))
        {
            _known[(processId, start)] = launcher = Walk(processId, start, Snapshot(), CreationTime);
        }

        return launcher;
    }

    /// <summary>
    /// Climbs from a process through up to 10 parents to the first listed launcher. The process itself never counts:
    /// Claude's desktop app is claude.exe too, and its own windows aren't an agent's.
    /// </summary>
    internal static string? Walk(uint processId, long start, IReadOnlyDictionary<uint, ProcessEntry> processes, Func<uint, long?> creationTime)
    {
        for (var level = 0; level < MaxParents && processes.TryGetValue(processId, out var process); level++)
        {
            // A parent that exited may have left its ID to a newer process, so a parent counts only if it started first.
            if (!processes.TryGetValue(process.ParentId, out var parent) || creationTime(process.ParentId) is not { } parentStart || parentStart >= start)
            {
                return null;
            }

            if (Names.Contains(parent.ExeName))
            {
                return parent.ExeName;
            }

            (processId, start) = (process.ParentId, parentStart);
        }

        return null;
    }

    private static unsafe Dictionary<uint, ProcessEntry> Snapshot()
    {
        var processes = new Dictionary<uint, ProcessEntry>();
        using var snapshot = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        var entry = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
        for (var more = PInvoke.Process32FirstW(snapshot, ref entry); more; more = PInvoke.Process32NextW(snapshot, ref entry))
        {
            processes[entry.th32ProcessID] = new(entry.th32ParentProcessID, entry.szExeFile.ToString());
        }

        return processes;
    }

    private static long? CreationTime(uint processId)
    {
        var process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsNull)
        {
            return null;
        }

        try
        {
            return WindowInfo.CreationTime(process);
        }
        finally
        {
            PInvoke.CloseHandle(process);
        }
    }
}

using System.Diagnostics;

namespace Staypaw.Tests;

/// <summary>A copy of cmd.exe named claude.exe, so whatever it runs has a listed launcher.</summary>
internal sealed class FakeAgent : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("staypaw-agent-");

    public FakeAgent(string command)
    {
        var claude = Path.Combine(_folder.FullName, "claude.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), claude);
        Launcher = Process.Start(new ProcessStartInfo(claude, "/c " + command) { UseShellExecute = false, CreateNoWindow = true })!;
    }

    public Process Launcher { get; }

    /// <summary>The first process with this name that started after the launcher.</summary>
    public Process WaitForChild(string name)
    {
        for (var i = 0; i < 50; i++)
        {
            if (Process.GetProcessesByName(name).FirstOrDefault(p => p.StartTime >= Launcher.StartTime) is { } child)
            {
                return child;
            }

            Thread.Sleep(100);
        }

        throw new TimeoutException($"The fake agent never started {name}.");
    }

    public void Dispose()
    {
        if (!Launcher.HasExited)
        {
            Launcher.Kill(entireProcessTree: true);
            Launcher.WaitForExit();
        }

        _folder.Delete(recursive: true);
    }
}

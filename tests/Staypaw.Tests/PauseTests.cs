using Staypaw.Core;

namespace Staypaw.Tests;

public class PauseTests
{
    // A forgotten pause must not leave you unprotected, so every pause ends by itself.
    [Fact]
    public void A_pause_ends_by_itself()
    {
        using var watcher = new FocusWatcher(new FocusLog(Path.Combine(Path.GetTempPath(), $"staypaw-pause-{Guid.NewGuid():N}.csv")));

        watcher.Pause(TimeSpan.FromMilliseconds(500));
        Assert.True(watcher.Paused);
        Thread.Sleep(600);
        Assert.False(watcher.Paused);
    }

    [Fact]
    public void A_pause_of_zero_resumes()
    {
        using var watcher = new FocusWatcher(new FocusLog(Path.Combine(Path.GetTempPath(), $"staypaw-pause-{Guid.NewGuid():N}.csv")));

        watcher.Pause(TimeSpan.FromMinutes(15));
        watcher.Pause(TimeSpan.Zero);
        Assert.False(watcher.Paused);
    }
}

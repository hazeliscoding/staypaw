using Staypaw.Core;

namespace Staypaw.Tests;

public sealed class InputTrackerTests : IDisposable
{
    private const ushort Alt = 0x12, Tab = 0x09, Esc = 0x1B, One = 0x31, Win = 0x5B;

    private readonly InputTracker _tracker = new();

    public void Dispose() => _tracker.Dispose();

    // FFXIV's hotbars use Alt with the number keys, so a stealer must not get through right after one.
    [Fact]
    public void Alt_with_a_number_key_is_not_a_switch()
    {
        Press(Alt, true, 100);
        Press(One, true, 110);
        Press(One, false, 150);
        Press(Alt, false, 200);

        Assert.Null(_tracker.LastSwitchKeyAtOrBefore(1000));
    }

    [Fact]
    public void Alt_Tab_is_a_switch_when_Alt_is_released()
    {
        Press(Alt, true, 100);
        Press(Tab, true, 110);
        Press(Tab, false, 150);
        Press(Alt, false, 600);

        Assert.Equal(600, _tracker.LastSwitchKeyAtOrBefore(1000));
    }

    [Fact]
    public void Alt_Esc_is_a_switch()
    {
        Press(Alt, true, 100);
        Press(Esc, true, 110);

        Assert.Equal(110, _tracker.LastSwitchKeyAtOrBefore(1000));
    }

    [Fact]
    public void The_Win_key_is_a_switch()
    {
        Press(Win, true, 100);

        Assert.Equal(100, _tracker.LastSwitchKeyAtOrBefore(1000));
    }

    [Fact]
    public void Tab_without_Alt_is_not_a_switch()
    {
        Press(Tab, true, 100);

        Assert.Null(_tracker.LastSwitchKeyAtOrBefore(1000));
    }

    private void Press(ushort vk, bool down, int time) => _tracker.OnKey(vk, down, time);
}

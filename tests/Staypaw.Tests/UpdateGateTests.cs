using Staypaw.Desktop;

namespace Staypaw.Tests;

public class UpdateGateTests
{
    [Fact]
    public void Restarts_at_once_when_the_protected_app_is_not_in_front()
    {
        var restarts = 0;
        var gate = new UpdateGate(() => false, () => restarts++);

        gate.Downloaded();

        Assert.Equal(1, restarts);
    }

    [Fact]
    public void Waits_while_the_protected_app_is_in_front_and_restarts_once_you_leave_it()
    {
        var inFront = true;
        var restarts = 0;
        var gate = new UpdateGate(() => inFront, () => restarts++);

        gate.Downloaded();
        gate.FocusChanged();
        Assert.Equal(0, restarts);

        inFront = false;
        gate.FocusChanged();
        gate.FocusChanged();
        Assert.Equal(1, restarts);
    }

    [Fact]
    public void Never_restarts_without_a_downloaded_update()
    {
        var restarts = 0;
        var gate = new UpdateGate(() => false, () => restarts++);

        gate.FocusChanged();

        Assert.Equal(0, restarts);
    }
}

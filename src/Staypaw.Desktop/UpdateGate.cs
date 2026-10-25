namespace Staypaw.Desktop;

/// <summary>
/// Restarts into a downloaded update only while the protected app isn't in front. Nothing guards it for the few
/// seconds a restart takes, and mid-game is when that matters most.
/// </summary>
public sealed class UpdateGate(Func<bool> protectedAppInFront, Action restart)
{
    private bool _waiting;

    public void Downloaded()
    {
        _waiting = true;
        FocusChanged();
    }

    public void FocusChanged()
    {
        if (_waiting && !protectedAppInFront())
        {
            _waiting = false;
            restart();
        }
    }
}

namespace Staypaw.Desktop;

/// <summary>
/// Restarts into a downloaded update only while the protected app isn't in front and no crash report is open. Nothing
/// guards the protected app for the few seconds a restart takes, and mid-game is when that matters most. A crash report
/// counts as shown once its window opens, so a restart then would close it unfiled for good.
/// </summary>
public sealed class UpdateGate(Func<bool> protectedAppInFront, Action restart)
{
    private bool _waiting;
    private bool _reportOpen;

    public void Downloaded()
    {
        _waiting = true;
        FocusChanged();
    }

    public void ReportOpened() => _reportOpen = true;

    public void ReportClosed()
    {
        _reportOpen = false;
        FocusChanged();
    }

    public void FocusChanged()
    {
        if (_waiting && !_reportOpen && !protectedAppInFront())
        {
            _waiting = false;
            restart();
        }
    }
}

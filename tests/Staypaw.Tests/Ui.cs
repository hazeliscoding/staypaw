using Avalonia.Headless;
using Staypaw.Desktop;

namespace Staypaw.Tests;

// Avalonia can be set up only once per process, and controls must be built on its UI thread,
// so every UI test dispatches through this one session.
internal static class Ui
{
    public static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.StartNew(typeof(App));
}

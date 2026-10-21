namespace Staypaw.Core;

/// <summary>What Staypaw thinks of one focus change. The words are the glossary's in AGENTS.md.</summary>
public enum Verdict
{
    /// <summary>Your switch, or a change that can't hurt (a dialog of your window, Windows' own UI).</summary>
    Yours,

    /// <summary>A focus change you didn't make.</summary>
    Steal,

    /// <summary>Right after a click in the window you were in: a link or file you opened, or a steal.</summary>
    Unclear,
}

public readonly record struct Classification(Verdict Verdict, string Reason);

/// <summary>Everything the classifier needs about one foreground change. Times are tick-count milliseconds.</summary>
public sealed record FocusChange
{
    public int EventTime { get; init; }

    public bool NextIsOwnProcess { get; init; }
    public bool NextIsSystemUI { get; init; }

    /// <summary>An invisible input-method window that should never have focus (a Windows 11 24H2 bug).</summary>
    public bool NextIsBogus { get; init; }

    public bool HasPrevious { get; init; }

    /// <summary>The window you were in was closed, hidden, minimized or moved to another desktop.</summary>
    public bool PreviousGone { get; init; }

    public bool SameProcess { get; init; }

    /// <summary>One window owns the other, like a dialog and its main window.</summary>
    public bool OwnerRelated { get; init; }

    /// <summary>The window you were in runs as administrator and Staypaw doesn't, so Windows hid your input there.</summary>
    public bool InputHiddenFromUs { get; init; }

    /// <summary>Windows' "activate the window under the mouse" (X-Mouse) is on and the new window is under the pointer.</summary>
    public bool FollowsMouse { get; init; }

    public int? LastClickOnNext { get; init; }

    /// <summary>A click on the taskbar, Start, search or a notification.</summary>
    public int? LastShellClick { get; init; }

    /// <summary>An Alt or Win key, as in Alt+Tab or Win+1.</summary>
    public int? LastSwitchKey { get; init; }

    /// <summary>Input Windows saw but Raw Input didn't report, such as touch or pen.</summary>
    public int? LastUnseenInput { get; init; }

    public int? LastClickInPrevious { get; init; }

    /// <summary>Your latest click or deliberate key anywhere, used to recognize an app you just launched.</summary>
    public int? LastAction { get; init; }

    public int? LastTyping { get; init; }

    /// <summary>When the process that now has the foreground started, if known.</summary>
    public int? NextProcessStart { get; init; }
}

/// <summary>
/// The rules from ROADMAP.md: a switch counts only if you made it, and there's no grace period after a keypress.
/// </summary>
public static class Classifier
{
    // A click or Alt+Tab activates its window within milliseconds; 1.5 s also covers a slow app.
    public const int SwitchWindowMs = 1500;
    public const int TypingWindowMs = 1500;
    public const int LaunchGraceMs = 10000;

    // An app that stole this recently loses the benefit of the doubt after your clicks.
    public const int RepeatStealerMs = 10 * 60 * 1000;

    /// <summary>
    /// Whether to put focus back. A steal is undone when it took focus from the protected app or from the window
    /// you were typing in. An Unclear change, usually a link or file you opened, is undone only for a repeat stealer.
    /// </summary>
    public static bool ShouldRestore(Classification classification, FocusChange c, bool previousIsProtected, bool nextIsRepeatStealer) =>
        classification.Verdict switch
        {
            Verdict.Steal => previousIsProtected || Within(c, c.LastTyping, TypingWindowMs),
            Verdict.Unclear => nextIsRepeatStealer,
            _ => false,
        };

    public static Classification Classify(FocusChange c)
    {
        if (c.NextIsOwnProcess)
        {
            return Yours("Staypaw window");
        }

        if (c.NextIsSystemUI)
        {
            return Yours("Windows system UI");
        }

        if (!c.HasPrevious)
        {
            return Yours("no previous window");
        }

        if (c.PreviousGone)
        {
            return Yours("previous window was closed or minimized");
        }

        if (c.SameProcess)
        {
            return Yours("same app");
        }

        if (c.OwnerRelated)
        {
            return Yours("dialog of the previous window");
        }

        if (c.NextIsBogus)
        {
            return new(Verdict.Steal, "an invisible input-method window took focus (Windows 11 bug)");
        }

        if (c.InputHiddenFromUs)
        {
            return Yours("an administrator app had focus, so your input there wasn't visible");
        }

        if (c.FollowsMouse)
        {
            return Yours("focus follows your mouse (X-Mouse)");
        }

        if (Within(c, c.LastClickOnNext, SwitchWindowMs))
        {
            return Yours("you clicked it");
        }

        if (Within(c, c.LastShellClick, SwitchWindowMs))
        {
            return Yours("you opened it from the taskbar, Start, search or a notification");
        }

        if (Within(c, c.LastSwitchKey, SwitchWindowMs))
        {
            return Yours("after Alt or Win (Alt+Tab or a Windows shortcut)");
        }

        if (Within(c, c.LastUnseenInput, SwitchWindowMs))
        {
            return Yours("after input Staypaw couldn't see (touch, pen or an accessibility tool)");
        }

        if (LaunchedAfter(c, c.LastAction))
        {
            return Yours("an app you just launched");
        }

        if (Within(c, c.LastClickInPrevious, SwitchWindowMs))
        {
            return new(Verdict.Unclear, "right after your click in the window you were in (it may have opened a link or file)");
        }

        return Within(c, c.LastTyping, TypingWindowMs)
            ? new(Verdict.Steal, "no click or switch asked for it, while you were typing")
            : new(Verdict.Steal, "no click or switch asked for it");
    }

    /// <summary>Milliseconds from <paramref name="earlier"/> to <paramref name="later"/>, safe across tick-count wraparound.</summary>
    public static int Elapsed(int later, int earlier) => unchecked(later - earlier);

    private static bool Within(FocusChange c, int? time, int windowMs) =>
        time.HasValue && Elapsed(c.EventTime, time.Value) <= windowMs;

    /// <summary>The new window's process started no earlier than a second before your action, and recently.</summary>
    private static bool LaunchedAfter(FocusChange c, int? action) =>
        action.HasValue && c.NextProcessStart.HasValue
        && Elapsed(c.EventTime, action.Value) <= LaunchGraceMs
        && Elapsed(c.NextProcessStart.Value, action.Value) >= -1000;

    private static Classification Yours(string reason) => new(Verdict.Yours, reason);
}

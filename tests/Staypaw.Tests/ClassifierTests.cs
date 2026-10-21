using Staypaw.Core;

namespace Staypaw.Tests;

public class ClassifierTests
{
    private const int Now = 100_000;

    // One ordinary app taking focus from another, with no input at all.
    private static readonly FocusChange Baseline = new() { EventTime = Now, HasPrevious = true };

    [Fact]
    public void A_change_nothing_asked_for_is_a_steal() => Assert.Equal(Verdict.Steal, VerdictOf(Baseline));

    [Fact]
    public void A_steal_while_you_type_says_so()
    {
        var result = Classifier.Classify(Baseline with { LastTyping = Now - 200 });

        Assert.Equal(Verdict.Steal, result.Verdict);
        Assert.Contains("while you were typing", result.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Clicking_the_window_is_your_switch() =>
        Assert.Equal(Verdict.Yours, VerdictOf(Baseline with { LastClickOnNext = Now - 5 }));

    [Fact]
    public void Opening_from_the_taskbar_or_Start_is_your_switch() =>
        Assert.Equal(Verdict.Yours, VerdictOf(Baseline with { LastShellClick = Now - 400 }));

    [Fact]
    public void Alt_Tab_is_your_switch() =>
        Assert.Equal(Verdict.Yours, VerdictOf(Baseline with { LastSwitchKey = Now - 30 }));

    [Fact]
    public void Input_Staypaw_could_not_see_counts_as_yours() =>
        Assert.Equal(Verdict.Yours, VerdictOf(Baseline with { LastUnseenInput = Now - 30 }));

    // NoFocusSteal lets any app take focus for 1.5 s after Enter, Tab, Esc or an F-key. Staypaw doesn't.
    [Fact]
    public void A_keypress_gives_no_grace_period() =>
        Assert.Equal(Verdict.Steal, VerdictOf(Baseline with { LastAction = Now - 100 }));

    [Fact]
    public void An_app_you_just_launched_is_yours() =>
        Assert.Equal(Verdict.Yours, VerdictOf(Baseline with { LastAction = Now - 3000, NextProcessStart = Now - 2500 }));

    [Fact]
    public void An_app_that_was_already_running_was_not_launched_by_you() =>
        Assert.Equal(Verdict.Steal, VerdictOf(Baseline with { LastAction = Now - 3000, NextProcessStart = Now - 60_000 }));

    [Fact]
    public void A_launch_that_waits_too_long_to_take_focus_is_a_steal() =>
        Assert.Equal(Verdict.Steal, VerdictOf(Baseline with { LastAction = Now - 11_000, NextProcessStart = Now - 10_500 }));

    [Fact]
    public void Right_after_a_click_in_your_window_is_unclear() =>
        Assert.Equal(Verdict.Unclear, VerdictOf(Baseline with { LastClickInPrevious = Now - 300 }));

    [Fact]
    public void A_switch_request_older_than_the_window_does_not_count() =>
        Assert.Equal(Verdict.Steal, VerdictOf(Baseline with { LastClickOnNext = Now - Classifier.SwitchWindowMs - 1 }));

    [Fact]
    public void An_input_method_window_is_a_steal_even_right_after_a_click() =>
        Assert.Equal(Verdict.Steal, VerdictOf(Baseline with { NextIsBogus = true, LastClickOnNext = Now - 5 }));

    [Fact]
    public void Times_work_across_tick_count_wraparound() =>
        Assert.Equal(Verdict.Yours, VerdictOf(Baseline with { EventTime = int.MinValue + 100, LastClickOnNext = int.MaxValue - 100 }));

    [Theory]
    [InlineData("own process")]
    [InlineData("system UI")]
    [InlineData("no previous window")]
    [InlineData("previous gone")]
    [InlineData("same process")]
    [InlineData("owner related")]
    [InlineData("input hidden")]
    [InlineData("follows mouse")]
    public void Changes_that_cannot_hurt_are_yours(string kind)
    {
        var change = kind switch
        {
            "own process" => Baseline with { NextIsOwnProcess = true },
            "system UI" => Baseline with { NextIsSystemUI = true },
            "no previous window" => Baseline with { HasPrevious = false },
            "previous gone" => Baseline with { PreviousGone = true },
            "same process" => Baseline with { SameProcess = true },
            "owner related" => Baseline with { OwnerRelated = true },
            "input hidden" => Baseline with { InputHiddenFromUs = true },
            "follows mouse" => Baseline with { FollowsMouse = true },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        Assert.Equal(Verdict.Yours, VerdictOf(change));
    }

    [Fact]
    public void A_steal_from_the_protected_app_is_restored_even_while_you_are_idle() =>
        Assert.True(Restores(Baseline, previousIsProtected: true));

    [Fact]
    public void A_steal_while_you_type_is_restored_in_any_app() =>
        Assert.True(Restores(Baseline with { LastTyping = Now - 300 }));

    // You type a line, press Enter, and a popup lands 100 ms later.
    [Fact]
    public void A_steal_right_after_a_keypress_is_restored() =>
        Assert.True(Restores(Baseline with { LastTyping = Now - 400, LastAction = Now - 100 }));

    [Fact]
    public void An_idle_steal_from_an_unprotected_app_is_only_logged() =>
        Assert.False(Restores(Baseline));

    [Fact]
    public void A_switch_to_the_window_you_clicked_is_never_restored() =>
        Assert.False(Restores(Baseline with { LastClickOnNext = Now - 5, LastTyping = Now - 300 }, previousIsProtected: true));

    [Fact]
    public void A_link_you_opened_is_allowed() =>
        Assert.False(Restores(Baseline with { LastClickInPrevious = Now - 300 }, previousIsProtected: true));

    [Fact]
    public void A_repeat_stealer_is_restored_even_right_after_your_click() =>
        Assert.True(Restores(Baseline with { LastClickInPrevious = Now - 300 }, nextIsRepeatStealer: true));

    private static Verdict VerdictOf(FocusChange change) => Classifier.Classify(change).Verdict;

    private static bool Restores(FocusChange change, bool previousIsProtected = false, bool nextIsRepeatStealer = false) =>
        Classifier.ShouldRestore(Classifier.Classify(change), change, previousIsProtected, nextIsRepeatStealer);
}

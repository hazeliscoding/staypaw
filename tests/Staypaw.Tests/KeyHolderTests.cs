using Staypaw.Core;

namespace Staypaw.Tests;

// Windows are plain numbers here: 1 is the window Staypaw last saw, 2 a stealer, 0 none.
public class KeyHolderTests
{
    private const int T0 = 1000;
    private static readonly HeldKey ADown = new(0x41, 0x1E, up: false, extended: false);
    private static readonly HeldKey AUp = new(0x41, 0x1E, up: true, extended: false);
    private static readonly HeldKey BDown = new(0x42, 0x30, up: false, extended: false);
    private static readonly HeldKey ShiftUp = new(0x10, 0x2A, up: true, extended: false);

    [Fact]
    public void Keys_pass_while_focus_is_where_Staypaw_last_saw_it()
    {
        var holder = new KeyHolder();

        Assert.False(holder.Offer(ADown, foreground: 1, judged: 1, T0));
        Assert.False(holder.Offer(ADown, foreground: 0, judged: 1, T0));
        Assert.False(holder.Holding);
    }

    [Fact]
    public void Keys_wait_once_focus_moves_and_come_back_in_order_for_your_window()
    {
        var holder = new KeyHolder();

        Assert.True(holder.Offer(ADown, foreground: 2, judged: 1, T0));
        Assert.True(holder.Offer(AUp, foreground: 2, judged: 1, T0 + 5));

        Assert.Equal([ADown, AUp], holder.Settle(settled: true, deliver: true, T0 + 10));
    }

    [Fact]
    public void A_key_up_whose_key_down_went_through_before_the_hold_passes()
    {
        var holder = new KeyHolder();

        Assert.False(holder.Offer(ShiftUp, foreground: 2, judged: 1, T0));
        Assert.True(holder.Offer(BDown, foreground: 2, judged: 1, T0 + 5));

        Assert.Equal([BDown], holder.Settle(settled: true, deliver: true, T0 + 10));
    }

    [Fact]
    public void Keys_for_a_window_that_is_not_yours_are_dropped()
    {
        var holder = new KeyHolder();
        holder.Offer(ADown, foreground: 2, judged: 1, T0);

        Assert.Null(holder.Settle(settled: true, deliver: false, T0 + 10));
        Assert.False(holder.Holding);
        Assert.False(holder.Offer(AUp, foreground: 2, judged: 2, T0 + 20));
    }

    [Fact]
    public void Keys_wait_while_a_restore_is_still_pending()
    {
        var holder = new KeyHolder();
        holder.Offer(ADown, foreground: 2, judged: 0, T0);

        Assert.Null(holder.Settle(settled: false, deliver: true, T0 + 100));
        Assert.True(holder.Holding);
        Assert.Equal([ADown], holder.Settle(settled: true, deliver: true, T0 + 150));
    }

    [Fact]
    public void A_hold_past_the_limit_is_dropped_and_not_restarted_for_the_same_window()
    {
        var holder = new KeyHolder();
        holder.Offer(ADown, foreground: 2, judged: 1, T0);

        Assert.False(holder.Offer(BDown, foreground: 2, judged: 1, T0 + KeyHolder.LimitMs + 1));
        Assert.False(holder.Holding);
        Assert.True(holder.Offer(BDown, foreground: 3, judged: 1, T0 + KeyHolder.LimitMs + 2));
    }

    [Fact]
    public void Settling_past_the_limit_drops_the_keys()
    {
        var holder = new KeyHolder();
        holder.Offer(ADown, foreground: 2, judged: 0, T0);

        Assert.Null(holder.Settle(settled: false, deliver: true, T0 + KeyHolder.LimitMs + 1));
        Assert.False(holder.Holding);
    }

    [Fact]
    public void Keys_typed_during_a_replay_follow_it()
    {
        var holder = new KeyHolder();
        holder.Offer(ADown, foreground: 2, judged: 1, T0);
        Assert.Equal([ADown], holder.Settle(settled: true, deliver: true, T0 + 10));

        Assert.True(holder.Offer(BDown, foreground: 1, judged: 1, T0 + 11));
        Assert.False(holder.OnReplayed(ADown, last: true, foreground: 1, judged: 1, T0 + 12, out var next));
        Assert.Equal([BDown], next);

        Assert.False(holder.OnReplayed(BDown, last: true, foreground: 1, judged: 1, T0 + 13, out next));
        Assert.Null(next);
        Assert.False(holder.Holding);
    }

    [Fact]
    public void The_limit_restarts_with_each_replay()
    {
        var holder = new KeyHolder();
        holder.Offer(ADown, foreground: 2, judged: 1, T0);
        holder.Settle(settled: true, deliver: true, T0 + 400);
        holder.Offer(BDown, foreground: 1, judged: 1, T0 + 450);
        holder.OnReplayed(ADown, last: true, foreground: 1, judged: 1, T0 + 460, out _);

        Assert.True(holder.Offer(AUp, foreground: 1, judged: 1, T0 + KeyHolder.LimitMs + 100));
    }

    [Fact]
    public void A_replayed_key_is_held_again_if_focus_moved_before_it_landed()
    {
        var holder = new KeyHolder();
        holder.Offer(ADown, foreground: 2, judged: 1, T0);
        holder.Offer(AUp, foreground: 2, judged: 1, T0 + 1);
        holder.Settle(settled: true, deliver: true, T0 + 10);
        holder.Offer(BDown, foreground: 1, judged: 1, T0 + 11);

        Assert.True(holder.OnReplayed(ADown, last: false, foreground: 3, judged: 1, T0 + 12, out _));
        Assert.True(holder.OnReplayed(AUp, last: true, foreground: 3, judged: 1, T0 + 12, out var next));
        Assert.Null(next);

        Assert.True(holder.Holding);
        Assert.Equal([ADown, AUp, BDown], holder.Settle(settled: true, deliver: true, T0 + 20));
    }

    [Fact]
    public void A_replay_that_lands_after_the_hold_ended_passes()
    {
        var holder = new KeyHolder();

        Assert.False(holder.OnReplayed(ADown, last: true, foreground: 2, judged: 1, T0, out var next));
        Assert.Null(next);
    }

    // Windows counts a held key as input though Raw Input never reports it, which would look like touch or pen.
    [Fact]
    public void A_held_key_counts_as_input_Staypaw_saw()
    {
        var holder = new KeyHolder();
        Assert.False(holder.Offer(ADown, foreground: 1, judged: 1, T0));
        Assert.False(holder.HeldNear(T0));

        holder.Offer(BDown, foreground: 2, judged: 1, T0 + 1000);

        Assert.True(holder.HeldNear(T0 + 1000));
        Assert.False(holder.HeldNear(T0 + 2000));
    }

    [Fact]
    public void A_replayed_key_held_again_counts_as_input_Staypaw_saw()
    {
        var holder = new KeyHolder();
        holder.Offer(ADown, foreground: 2, judged: 1, T0);
        holder.Settle(settled: true, deliver: true, T0 + 10);

        holder.OnReplayed(ADown, last: true, foreground: 3, judged: 1, T0 + 1000, out _);

        Assert.True(holder.HeldNear(T0 + 1000));
    }

    [Fact]
    public void A_hold_with_nothing_in_it_ends_without_a_replay()
    {
        var holder = new KeyHolder();
        holder.Offer(ShiftUp, foreground: 2, judged: 1, T0);

        Assert.Null(holder.Settle(settled: true, deliver: true, T0 + 10));
        Assert.False(holder.Holding);
    }
}

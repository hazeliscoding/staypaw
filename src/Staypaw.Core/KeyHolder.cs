namespace Staypaw.Core;

/// <summary>
/// One key event as the low-level keyboard hook reports it. Held keys are the trust contract's one exception: kept in
/// memory until they're replayed or dropped, and never written anywhere. A plain struct, so its ToString is only the
/// type name and a trace line can't leak a key.
/// </summary>
internal readonly struct HeldKey(ushort vk, ushort scan, bool up, bool extended)
{
    public ushort Vk { get; } = vk;
    public ushort Scan { get; } = scan;
    public bool Up { get; } = up;
    public bool Extended { get; } = extended;
}

/// <summary>
/// Holds keys typed while focus is on a window Staypaw hasn't judged yet, then hands them back to replay into your
/// window, or drops them. Windows are passed in as handles and times as tick counts; it calls no Win32. The hook's
/// thread and the watcher's both call it, so every change happens under one lock.
/// </summary>
internal sealed class KeyHolder
{
    // A restore takes a few milliseconds. A hold this old means the watcher is busy or losing a fight, and then your
    // keyboard matters more than the keys.
    public const int LimitMs = 500;

    private readonly Lock _gate = new();
    private readonly List<HeldKey> _keys = [];
    private readonly HashSet<ushort> _downs = [];
    private volatile bool _holding;
    private bool _replaying;
    private int _since;
    private int _heldAgain;
    private nint _gaveUpOn;
    private volatile int _lastHeldAt;

    /// <summary>True from the first held key until the keys are replayed or dropped. Safe to read without the lock.</summary>
    public bool Holding => _holding;

    /// <summary>
    /// True if a key was held around <paramref name="time"/>, in the window <see cref="InputTracker.SawInputNear"/>
    /// uses. Windows counts a held key as input though Raw Input never reports it, so it would pass for touch or pen.
    /// </summary>
    public bool HeldNear(int time) => Classifier.Elapsed(_lastHeldAt, time) is >= -150 and <= 60;

    /// <summary>
    /// True to hold a key you typed: focus is on <paramref name="foreground"/>, and the watcher last saw
    /// <paramref name="judged"/>, so a move it hasn't judged may be a steal.
    /// </summary>
    public bool Offer(HeldKey key, nint foreground, nint judged, int now)
    {
        lock (_gate)
        {
            if (_holding && Classifier.Elapsed(now, _since) > LimitMs)
            {
                _gaveUpOn = foreground;
                End();
            }

            if (!_holding)
            {
                if (foreground == 0 || foreground == judged || foreground == _gaveUpOn)
                {
                    return false;
                }

                _holding = true;
                _since = now;
                _gaveUpOn = 0;
            }

            // A key-up whose key-down went through before the hold passes too: dropping it would leave the key down.
            if (key.Up && !_downs.Contains(key.Vk))
            {
                return false;
            }

            if (!key.Up)
            {
                _downs.Add(key.Vk);
            }

            _keys.Add(key);
            _lastHeldAt = now;
            return true;
        }
    }

    /// <summary>
    /// After the watcher judged a change: the keys to replay into the foreground window if <paramref name="deliver"/>,
    /// otherwise null, and they're dropped. Until <paramref name="settled"/>, a change is unjudged or a restore is
    /// pending, so the keys wait, up to the limit.
    /// </summary>
    public HeldKey[]? Settle(bool settled, bool deliver, int now)
    {
        lock (_gate)
        {
            if (!_holding)
            {
                return null;
            }

            if (Classifier.Elapsed(now, _since) > LimitMs)
            {
                End();
                return null;
            }

            if (_replaying || !settled)
            {
                return null;
            }

            if (!deliver || _keys.Count == 0)
            {
                End();
                return null;
            }

            return TakeBatch(now);
        }
    }

    /// <summary>
    /// One of Staypaw's replayed keys came back through the hook. True to hold it again, because focus moved before it
    /// landed. After the batch's last key, <paramref name="next"/> is any keys typed meanwhile, to replay in turn.
    /// </summary>
    public bool OnReplayed(HeldKey key, bool last, nint foreground, nint judged, int now, out HeldKey[]? next)
    {
        next = null;
        lock (_gate)
        {
            if (!_replaying)
            {
                return false;
            }

            // Ahead of keys typed during the replay, which came after it.
            var again = foreground != 0 && foreground != judged;
            if (again)
            {
                _keys.Insert(_heldAgain++, key);
                _lastHeldAt = now;
            }

            if (last)
            {
                _replaying = false;
                if (_heldAgain > 0)
                {
                    _since = now;
                }
                else if (_keys.Count > 0)
                {
                    next = TakeBatch(now);
                }
                else
                {
                    End();
                }
            }

            return again;
        }
    }

    /// <summary>Drops the held keys, for a replay Windows refused.</summary>
    public void Drop()
    {
        lock (_gate)
        {
            End();
        }
    }

    private HeldKey[] TakeBatch(int now)
    {
        var batch = _keys.ToArray();
        _keys.Clear();
        _heldAgain = 0;
        _replaying = true;

        // The limit is for a hold that's stuck; one that's replaying is making progress.
        _since = now;
        return batch;
    }

    private void End()
    {
        _keys.Clear();
        _downs.Clear();
        _heldAgain = 0;
        _replaying = false;
        _holding = false;
    }
}

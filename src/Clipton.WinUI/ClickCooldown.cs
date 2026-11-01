namespace Clipton.WinUI;

/// <summary>
/// Rejects repeated activations that arrive within <c>duration</c> of the previous accepted one.
/// Buttons that start network or Store work use it so rapid clicks cannot queue duplicate requests.
/// </summary>
internal sealed class ClickCooldown
{
    private readonly long _durationMilliseconds;
    private readonly Func<long> _nowMilliseconds;
    private long _blockedUntil;

    public ClickCooldown(TimeSpan duration, Func<long>? nowMilliseconds = null)
    {
        _durationMilliseconds = (long)duration.TotalMilliseconds;
        _nowMilliseconds = nowMilliseconds ?? (() => Environment.TickCount64);
    }

    public TimeSpan Duration => TimeSpan.FromMilliseconds(_durationMilliseconds);

    public bool IsCoolingDown => _nowMilliseconds() < Interlocked.Read(ref _blockedUntil);

    public bool TryEnter()
    {
        while (true)
        {
            var now = _nowMilliseconds();
            var blockedUntil = Interlocked.Read(ref _blockedUntil);
            if (now < blockedUntil)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _blockedUntil, now + _durationMilliseconds, blockedUntil) == blockedUntil)
            {
                return true;
            }
        }
    }

    public void Restart()
    {
        Interlocked.Exchange(ref _blockedUntil, _nowMilliseconds() + _durationMilliseconds);
    }
}

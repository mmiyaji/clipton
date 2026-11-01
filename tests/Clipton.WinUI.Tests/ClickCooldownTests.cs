using Clipton.WinUI;

namespace Clipton.WinUI.Tests;

public sealed class ClickCooldownTests
{
    [Fact]
    public void TryEnter_RejectsRepeatedClicksUntilTheDurationHasPassed()
    {
        var now = 1_000L;
        var cooldown = new ClickCooldown(TimeSpan.FromSeconds(3), () => now);

        Assert.True(cooldown.TryEnter());
        now += 2_999;
        Assert.False(cooldown.TryEnter());
        Assert.True(cooldown.IsCoolingDown);
        now += 1;
        Assert.False(cooldown.IsCoolingDown);
        Assert.True(cooldown.TryEnter());
    }

    [Fact]
    public void Restart_ExtendsTheCooldownFromTheCurrentTime()
    {
        var now = 0L;
        var cooldown = new ClickCooldown(TimeSpan.FromSeconds(3), () => now);
        Assert.True(cooldown.TryEnter());

        now = 2_500;
        cooldown.Restart();
        now = 5_000;

        Assert.True(cooldown.IsCoolingDown);
        now = 5_500;
        Assert.False(cooldown.IsCoolingDown);
    }

    [Fact]
    public void TryEnter_AcceptsOnlyOneOfManyConcurrentClicks()
    {
        var cooldown = new ClickCooldown(TimeSpan.FromMinutes(1));
        var accepted = 0;

        Parallel.For(0, 200, _ =>
        {
            if (cooldown.TryEnter())
            {
                Interlocked.Increment(ref accepted);
            }
        });

        Assert.Equal(1, accepted);
    }
}

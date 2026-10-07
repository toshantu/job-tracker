using System.Threading.RateLimiting;
using JobTracker.Api.Ai;
using Xunit;

namespace JobTracker.Api.Tests;

public class KeywordGapRateLimiterTests
{
    private static KeywordGapRateLimiter Create(int perUser, int shared) =>
        new(Microsoft.Extensions.Options.Options.Create(new KeywordGapLimitsOptions
        {
            WindowMinutes = 10,
            PerUserPermits = perUser,
            GlobalPermits = shared
        }));

    private static bool Try(KeywordGapRateLimiter limiter, int userId)
    {
        using var lease = limiter.TryAcquire(userId);
        return lease.IsAcquired;
    }

    [Fact]
    public void One_users_limit_does_not_block_another()
    {
        using var limiter = Create(perUser: 2, shared: 10);

        Assert.True(Try(limiter, 1));
        Assert.True(Try(limiter, 1));
        Assert.False(Try(limiter, 1));

        Assert.True(Try(limiter, 2));
    }

    [Fact]
    public void The_shared_limit_applies_to_everyone()
    {
        using var limiter = Create(perUser: 2, shared: 3);

        Assert.True(Try(limiter, 1));
        Assert.True(Try(limiter, 1));
        Assert.True(Try(limiter, 2));

        Assert.False(Try(limiter, 3));
        Assert.False(Try(limiter, 2));
    }

    [Fact]
    public void A_user_over_their_own_limit_does_not_use_up_shared_permits()
    {
        using var limiter = Create(perUser: 1, shared: 2);

        Assert.True(Try(limiter, 1));

        // Refused by the user's own window. None of these may cost a shared permit.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Assert.False(Try(limiter, 1));
        }

        Assert.True(Try(limiter, 2));
        Assert.False(Try(limiter, 3));
    }

    [Fact]
    public void Tells_a_blocked_user_how_long_to_wait()
    {
        using var limiter = Create(perUser: 1, shared: 5);

        Assert.True(Try(limiter, 1));

        using var refused = limiter.TryAcquire(1);

        Assert.False(refused.IsAcquired);
        Assert.True(refused.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
    }

    [Fact]
    public void Tells_everyone_how_long_to_wait_when_the_shared_limit_is_used_up()
    {
        using var limiter = Create(perUser: 1, shared: 2);

        Assert.True(Try(limiter, 1));
        Assert.True(Try(limiter, 2));

        using var refused = limiter.TryAcquire(3);

        Assert.False(refused.IsAcquired);
        Assert.True(refused.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
    }
}

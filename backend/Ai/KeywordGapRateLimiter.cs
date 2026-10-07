using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace JobTracker.Api.Ai;

// One chained limiter: the caller's own bucket first, then a bucket shared by everyone.
// Each bucket allows that many requests at once and refills evenly across the window, so
// "4 per 10 minutes" means a burst of 4, then one more every 2.5 minutes. A refused request
// is told how long until the next token. (Sliding windows report no retry time, so they are not used.)
// Time-based limiters do not hand a permit back when a later limiter in the chain refuses, so the order
// matters: a user who is over their own limit never uses up a shared permit.
// Counters live in memory, so they reset whenever the server restarts or spins down.
public sealed class KeywordGapRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<int> _limiter;

    public KeywordGapRateLimiter(IOptions<KeywordGapLimitsOptions> options)
    {
        var limits = options.Value;
        var window = TimeSpan.FromMinutes(limits.WindowMinutes);

        var perUser = PartitionedRateLimiter.Create<int, int>(userId =>
            RateLimitPartition.GetTokenBucketLimiter(userId, _ => Bucket(limits.PerUserPermits, window)));

        var shared = PartitionedRateLimiter.Create<int, string>(_ =>
            RateLimitPartition.GetTokenBucketLimiter("shared", _ => Bucket(limits.GlobalPermits, window)));

        _limiter = PartitionedRateLimiter.CreateChained(perUser, shared);
    }

    public RateLimitLease TryAcquire(int userId) => _limiter.AttemptAcquire(userId);

    public void Dispose() => _limiter.Dispose();

    private static TokenBucketRateLimiterOptions Bucket(int permits, TimeSpan window) => new()
    {
        TokenLimit = permits,
        TokensPerPeriod = 1,
        ReplenishmentPeriod = TimeSpan.FromTicks(window.Ticks / permits),
        QueueLimit = 0,
        AutoReplenishment = true
    };
}

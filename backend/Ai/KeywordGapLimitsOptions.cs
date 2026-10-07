namespace JobTracker.Api.Ai;

// Input caps and rate limits for the keyword-gap endpoint. All of it is configuration so tests can shrink it.
// PerUserPermits and GlobalPermits are "this many per WindowMinutes": that many can be used at once,
// and they refill evenly across the window.
public class KeywordGapLimitsOptions
{
    public int MaxJobDescriptionChars { get; set; }
    public int MaxCvChars { get; set; }
    public int MaxBodyBytes { get; set; }
    public int WindowMinutes { get; set; }
    public int PerUserPermits { get; set; }
    public int GlobalPermits { get; set; }
}

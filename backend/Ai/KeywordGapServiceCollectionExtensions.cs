namespace JobTracker.Api.Ai;

public static class KeywordGapServiceCollectionExtensions
{
    public static IServiceCollection AddKeywordGap(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KeywordGapLimitsOptions>()
            .Bind(configuration.GetSection("Ai:KeywordGap"))
            .Validate(o => o.MaxJobDescriptionChars is >= 100 and <= 20000, "Ai:KeywordGap:MaxJobDescriptionChars must be between 100 and 20000")
            .Validate(o => o.MaxCvChars is >= 100 and <= 20000, "Ai:KeywordGap:MaxCvChars must be between 100 and 20000")
            .Validate(o => o.MaxBodyBytes is >= 1024 and <= 262144, "Ai:KeywordGap:MaxBodyBytes must be between 1024 and 262144")
            .Validate(o => o.MaxBodyBytes >= o.MaxJobDescriptionChars + o.MaxCvChars, "Ai:KeywordGap:MaxBodyBytes must be at least MaxJobDescriptionChars plus MaxCvChars")
            .Validate(o => o.WindowMinutes is >= 1 and <= 60, "Ai:KeywordGap:WindowMinutes must be between 1 and 60")
            .Validate(o => o.PerUserPermits is >= 1 and <= 100, "Ai:KeywordGap:PerUserPermits must be between 1 and 100")
            .Validate(o => o.GlobalPermits > o.PerUserPermits && o.GlobalPermits <= 1000, "Ai:KeywordGap:GlobalPermits must be greater than PerUserPermits and at most 1000")
            .ValidateOnStart();

        services.AddSingleton<KeywordGapRateLimiter>();
        services.AddSingleton<IKeywordGapAnalyzer, KeywordGapAnalyzer>();

        return services;
    }
}

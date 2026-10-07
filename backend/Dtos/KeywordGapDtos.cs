namespace JobTracker.Api.Dtos;

public enum KeywordImportance
{
    Required,
    Preferred
}

public sealed record KeywordResult(string Keyword, KeywordImportance Importance, bool InCv);

public sealed record KeywordGapGroupSummary(int Total, int Matched, int? Percent);

public sealed record KeywordGapSummary(KeywordGapGroupSummary Required, KeywordGapGroupSummary Preferred);

public sealed record KeywordGapResponse(IReadOnlyList<KeywordResult> Keywords, KeywordGapSummary Summary)
{
    // Counts and percentages are computed here from the validated list. Nothing is taken from the model.
    public static KeywordGapResponse FromKeywords(IReadOnlyList<KeywordResult> keywords)
    {
        var summary = new KeywordGapSummary(
            Summarise(keywords, KeywordImportance.Required),
            Summarise(keywords, KeywordImportance.Preferred));

        return new KeywordGapResponse(keywords, summary);
    }

    private static KeywordGapGroupSummary Summarise(IReadOnlyList<KeywordResult> keywords, KeywordImportance importance)
    {
        var group = keywords.Where(k => k.Importance == importance).ToList();
        var matched = group.Count(k => k.InCv);
        int? percent = group.Count == 0
            ? null
            : (int)Math.Round(100.0 * matched / group.Count, MidpointRounding.AwayFromZero);

        return new KeywordGapGroupSummary(group.Count, matched, percent);
    }
}

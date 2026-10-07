namespace JobTracker.Api.Dtos;

public enum KeywordImportance
{
    Required,
    Preferred
}

// What the browser sends. Unknown properties are ignored, so a user id in the body is never read.
public sealed record KeywordGapRequest(string? JobDescription, string? CvText);

// What the browser gets back when something goes wrong. Code is the stable part: the UI keys on it.
// Message is a short fixed sentence of ours and never contains provider text.
public sealed record KeywordGapError(
    string Code,
    string Message,
    int? RetryAfterSeconds = null,
    string? Field = null,
    int? MaxLength = null);

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

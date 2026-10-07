using System.Globalization;
using System.Text;
using System.Text.Json;
using JobTracker.Api.Dtos;

namespace JobTracker.Api.Ai;

// Model output is untrusted. Only the three fields we ask for are read. Everything else is dropped.
public static class ModelOutputParser
{
    private const int MaxKeywordLength = 60;

    public static bool TryParse(string? json, out List<KeywordResult> keywords)
    {
        keywords = new List<KeywordResult>();

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("keywords", out var array) ||
                array.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in array.EnumerateArray())
            {
                if (keywords.Count >= KeywordGapPrompt.MaxKeywords)
                {
                    break;
                }

                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("keyword", out var keywordElement) ||
                    keywordElement.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("importance", out var importanceElement) ||
                    importanceElement.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("inCv", out var inCvElement) ||
                    inCvElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    continue;
                }

                KeywordImportance importance;
                switch (importanceElement.GetString()?.Trim().ToLowerInvariant())
                {
                    case "required":
                        importance = KeywordImportance.Required;
                        break;
                    case "preferred":
                        importance = KeywordImportance.Preferred;
                        break;
                    default:
                        continue;
                }

                var keyword = Clean(keywordElement.GetString());
                if (keyword.Length == 0 || keyword.Length > MaxKeywordLength || !seen.Add(keyword))
                {
                    continue;
                }

                keywords.Add(new KeywordResult(keyword, importance, inCvElement.GetBoolean()));
            }

            return true;
        }
        catch (JsonException)
        {
            keywords = new List<KeywordResult>();
            return false;
        }
    }

    // Collapses whitespace and drops control and invisible formatting characters.
    private static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!previousWasSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                previousWasSpace = true;
                continue;
            }

            if (char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format)
            {
                continue;
            }

            builder.Append(c);
            previousWasSpace = false;
        }

        return builder.ToString().TrimEnd();
    }
}

using System.Text.RegularExpressions;

namespace JobTracker.Api.Ai;

public sealed record RedactionResult(string Text, int EmailsRemoved, int PhonesRemoved, int LinksRemoved);

// Best-effort data minimisation. Removes links, email addresses and phone numbers from CV text
// before it leaves the server. It does not remove names, employers, addresses or anything else.
public static class CvRedactor
{
    public const string LinkToken = "[link removed]";
    public const string EmailToken = "[email removed]";
    public const string PhoneToken = "[phone removed]";

    // NonBacktracking keeps matching time linear in the input size, whatever the text contains.
    private const RegexOptions Options = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking;

    private static readonly Regex LinkPattern = new(
        @"(?:https?://|www\.)[^\s<>""')\]]+|\b(?:linkedin\.com|github\.com|gitlab\.com|bitbucket\.org|twitter\.com|x\.com|medium\.com|stackoverflow\.com)/[^\s<>""')\]]*",
        Options);

    private static readonly Regex EmailPattern = new(
        @"[a-z0-9._%+\-]+@[a-z0-9\-]+(?:\.[a-z0-9\-]+)+",
        Options);

    // Digit groups of one to six digits, joined by at most one separator. Newlines never join groups.
    private static readonly Regex PhonePattern = new(
        @"\(?\+?\(?\d{1,6}(?:\)?[ \u00A0.\-]?\(?\d{1,6})+\)?",
        Options);

    public static RedactionResult Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new RedactionResult(string.Empty, 0, 0, 0);
        }

        var links = 0;
        var emails = 0;
        var phones = 0;

        var withoutLinks = LinkPattern.Replace(text, _ =>
        {
            links++;
            return LinkToken;
        });

        var withoutEmails = EmailPattern.Replace(withoutLinks, _ =>
        {
            emails++;
            return EmailToken;
        });

        var withoutPhones = PhonePattern.Replace(withoutEmails, match =>
        {
            if (!LooksLikePhoneNumber(withoutEmails, match))
            {
                return match.Value;
            }

            phones++;
            return PhoneToken;
        });

        return new RedactionResult(withoutPhones, emails, phones, links);
    }

    private static bool LooksLikePhoneNumber(string input, Match match)
    {
        // Part of a longer token such as an identifier: leave it alone.
        if (match.Index > 0 && char.IsLetterOrDigit(input[match.Index - 1]))
        {
            return false;
        }

        var end = match.Index + match.Length;
        if (end < input.Length && char.IsLetter(input[end]))
        {
            return false;
        }

        var groups = Regex.Split(match.Value, @"\D+", RegexOptions.CultureInvariant)
            .Where(group => group.Length > 0)
            .ToList();

        var digitCount = groups.Sum(group => group.Length);
        if (digitCount is < 9 or > 15)
        {
            return false;
        }

        // A run of four-digit years such as "2019 2021 2022".
        if (groups.All(group => group.Length == 4 &&
                                (group.StartsWith("19", StringComparison.Ordinal) || group.StartsWith("20", StringComparison.Ordinal))))
        {
            return false;
        }

        // Only dots between the digits: version numbers, dates and IP addresses,
        // unless the groups are laid out like a phone number such as 415.555.0132.
        var onlyDots = match.Value.Contains('.') &&
                       !match.Value.Any(c => c is ' ' or '\u00A0' or '(' or ')' or '+' or '-');
        if (onlyDots && (groups.Count >= 4 || groups.Any(group => group.Length <= 2)))
        {
            return false;
        }

        return true;
    }
}

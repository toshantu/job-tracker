using Microsoft.Extensions.Options;

namespace JobTracker.Api.Tests;

// Starts the API with one setting changed and returns the options-validation error that stopped it.
//
// The test host runs the app's startup on a background thread. When startup fails very fast, that thread can
// dispose the host before the test thread has finished attaching to it, and the test thread then gets an
// ObjectDisposedException instead of the real error. Nothing is wrong with the app in that case, so this helper
// tries again with a new factory. Every other outcome is final. When no validation error is found, the failure
// message lists the exception types and messages that were seen, so a real problem is never hidden.
internal static class StartupFailure
{
    private const int MaxAttempts = 5;

    public static OptionsValidationException Capture(string key, string value)
    {
        var seen = new List<string>();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var factory = new ApiFactory(key, value);

            var exception = Record.Exception(() => { _ = factory.Services; });
            var validation = FindOptionsValidationException(exception);

            if (validation is not null)
            {
                return validation;
            }

            seen.Add($"attempt {attempt}: {Describe(exception)}");

            if (!HasDisposedHost(exception))
            {
                break;
            }
        }

        throw new InvalidOperationException(
            $"Setting {key} to \"{value}\" should have stopped the host with an options validation error, but it did not. {string.Join(" | ", seen)}");
    }

    private static string Describe(Exception? exception)
    {
        if (exception is null)
        {
            return "the host started without any exception";
        }

        var parts = new List<string>();
        Collect(exception, parts);
        return string.Join(" -> ", parts);
    }

    private static void Collect(Exception exception, List<string> parts)
    {
        parts.Add($"{exception.GetType().FullName}: {exception.Message.ReplaceLineEndings(" ")}");

        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                Collect(inner, parts);
            }
        }
        else if (exception.InnerException is not null)
        {
            Collect(exception.InnerException, parts);
        }
    }

    private static bool HasDisposedHost(Exception? exception) =>
        exception switch
        {
            null => false,
            ObjectDisposedException => true,
            AggregateException aggregate => aggregate.InnerExceptions.Any(HasDisposedHost),
            _ => HasDisposedHost(exception.InnerException),
        };

    private static OptionsValidationException? FindOptionsValidationException(Exception? exception)
    {
        if (exception is null)
        {
            return null;
        }

        if (exception is OptionsValidationException validation)
        {
            return validation;
        }

        if (exception is AggregateException aggregate)
        {
            return aggregate.InnerExceptions
                .Select(FindOptionsValidationException)
                .FirstOrDefault(found => found is not null);
        }

        return FindOptionsValidationException(exception.InnerException);
    }
}

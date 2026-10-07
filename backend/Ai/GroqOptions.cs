namespace JobTracker.Api.Ai;

public class GroqOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ReasoningEffort { get; set; } = string.Empty;
    public int MaxCompletionTokens { get; set; }
    public int TimeoutSeconds { get; set; }
}
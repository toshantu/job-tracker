namespace JobTracker.Api.Auth;

public record GitHubOAuthCorrelationData(string CodeVerifier, string State);
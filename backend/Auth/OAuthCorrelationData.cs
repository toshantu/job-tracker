namespace JobTracker.Api.Auth;

public record OAuthCorrelationData(string CodeVerifier, string State, string Nonce);
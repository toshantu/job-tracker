using System.Net;
using System.Net.Http;
using System.Text;

namespace JobTracker.Api.Tests;

public sealed record CapturedRequest(
    HttpMethod Method,
    Uri? Uri,
    string? AuthorizationScheme,
    string? AuthorizationParameter,
    string Body);

public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly object _gate = new();
    private readonly List<CapturedRequest> _calls = new();

    public Func<CapturedRequest, HttpResponseMessage> Responder { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("{}", Encoding.UTF8, "application/json")
    };

    public IReadOnlyList<CapturedRequest> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToList();
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

        var captured = new CapturedRequest(
            request.Method,
            request.RequestUri,
            request.Headers.Authorization?.Scheme,
            request.Headers.Authorization?.Parameter,
            body);

        lock (_gate)
        {
            _calls.Add(captured);
        }

        return Responder(captured);
    }
}

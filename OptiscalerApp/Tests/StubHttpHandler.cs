using System.Net;

namespace Optiscaler.Tests;

/// <summary>Answers every request locally, so tests never depend on GitHub being reachable.</summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public int Requests { get; private set; }

    public static StubHttpHandler Offline()
    {
        return new StubHttpHandler(_ => throw new HttpRequestException("offline"));
    }

    public static StubHttpHandler Text(string body)
    {
        return new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
                                                           CancellationToken cancellationToken)
    {
        Requests++;

        return Task.FromResult(respond(request));
    }
}

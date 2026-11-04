using System.Net;
using System.Text;

namespace XlyFinance.Tests.Fakes;

/// <summary>
/// Records the requests it sees and answers with whatever the test prepared, so HTTP clients can be
/// exercised without touching the network.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpResponseMessage _response;

    private StubHttpMessageHandler(HttpResponseMessage response)
    {
        _response = response;
    }

    public List<Uri> RequestUris { get; } = [];

    public static StubHttpMessageHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    public static StubHttpMessageHandler NotJson(string body, HttpStatusCode status) =>
        new(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/html"),
        });

    public static StubHttpMessageHandler Empty(HttpStatusCode status) =>
        new(new HttpResponseMessage(status)
        {
            Content = new ByteArrayContent([]),
        });

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUris.Add(request.RequestUri!);
        return Task.FromResult(_response);
    }
}

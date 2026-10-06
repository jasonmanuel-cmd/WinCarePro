using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using WinCareDesktop.Services;
using Xunit;

namespace WinCareDesktop.Tests;

public class UpdateCheckerTests
{
    private class StaticHandler : HttpMessageHandler
    {
        private readonly string _body;
        public StaticHandler(string body) => _body = body;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, System.Threading.CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            });
    }

    [Fact]
    public async Task Newer_remote_version_is_detected()
    {
        var http = new HttpClient(new StaticHandler("{\"version\":\"1.1.0\"}"));
        var svc = new UpdateChecker(http);
        var r = await svc.CheckAsync("https://example.invalid/version.json", "1.0.0");
        Assert.True(r.IsUpdateAvailable);
        Assert.Equal("1.1.0", r.LatestVersion);
        Assert.Null(r.Error);
    }

    [Fact]
    public async Task Same_version_is_not_an_update()
    {
        var http = new HttpClient(new StaticHandler("{\"version\":\"1.0.0\"}"));
        var svc = new UpdateChecker(http);
        var r = await svc.CheckAsync("https://example.invalid/version.json", "1.0.0");
        Assert.False(r.IsUpdateAvailable);
    }

    [Fact]
    public async Task Missing_version_key_is_an_error_not_a_crash()
    {
        var http = new HttpClient(new StaticHandler("{\"hello\":\"world\"}"));
        var svc = new UpdateChecker(http);
        var r = await svc.CheckAsync("https://example.invalid/version.json", "1.0.0");
        Assert.False(r.IsUpdateAvailable);
        Assert.NotNull(r.Error);
    }
}

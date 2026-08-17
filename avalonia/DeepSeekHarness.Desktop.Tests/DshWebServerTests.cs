using DeepSeekHarness.Desktop;
using Xunit;

namespace DeepSeekHarness.Desktop.Tests;

public sealed class DshWebServerTests
{
    [Fact]
    public void ParsesLoopbackWebUrlFromStandardOutput()
    {
        var parsed = WebServerStartupOutput.TryParseWebUrl("dsh web: http://127.0.0.1:49152", out var url);

        Assert.True(parsed);
        Assert.Equal("http://127.0.0.1:49152/", url.AbsoluteUri);
    }

    [Fact]
    public async Task MissingUrlReturnsFailureWithStandardError()
    {
        var output = new WebServerStartupOutput();
        await output.ConsumeStandardOutputAsync("starting server");
        await output.ConsumeStandardErrorAsync("port is unavailable");

        Assert.Equal("DeepSeek Harness 未输出 Web 地址。\r\n\r\nport is unavailable", output.FailureMessage);
        Assert.False(output.WaitForUrlAsync().IsCompleted);
    }

    [Fact]
    public async Task RetryStopsThePreviousServerBeforeStartingReplacement()
    {
        var first = new FakeServer(new Uri("http://127.0.0.1:3001"));
        var second = new FakeServer(new Uri("http://127.0.0.1:3002"));
        var servers = new Queue<IDshWebServer>([first, second]);
        await using var host = new DesktopWebHost(() => servers.Dequeue());

        await host.StartAsync(CancellationToken.None);
        await host.StartAsync(CancellationToken.None);

        Assert.True(first.Stopped);
        Assert.False(second.Stopped);
    }

    private sealed class FakeServer(Uri address) : IDshWebServer
    {
        public bool Stopped { get; private set; }

        public Task<Uri> StartAsync(CancellationToken cancellationToken) => Task.FromResult(address);

        public Task StopAsync()
        {
            Stopped = true;
            return Task.CompletedTask;
        }
    }
}

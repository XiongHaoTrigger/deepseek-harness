namespace DeepSeekHarness.Desktop;

public sealed class DesktopWebHost : IAsyncDisposable
{
    private readonly Func<IDshWebServer> createServer;
    private IDshWebServer? server;

    public DesktopWebHost(Func<IDshWebServer> createServer)
    {
        this.createServer = createServer;
    }

    public async Task<Uri> StartAsync(CancellationToken cancellationToken)
    {
        await StopAsync();
        server = createServer();

        try
        {
            return await server.StartAsync(cancellationToken);
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        var activeServer = Interlocked.Exchange(ref server, null);
        if (activeServer is not null)
        {
            await activeServer.StopAsync();
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}

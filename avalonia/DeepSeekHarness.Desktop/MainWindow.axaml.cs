using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace DeepSeekHarness.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly DesktopWebHost webHost = new(() => new DshWebServer(DshWebLaunchResolver.Resolve()));
    private bool closing;

    public MainWindow()
    {
        InitializeComponent();

        if (Design.IsDesignMode)
        {
            StatusText.Text = "正在启动 DeepSeek Harness…";
            return;
        }

        Opened += HandleOpened;
        Closing += HandleClosing;
        RetryButton.Click += HandleRetry;
    }

    private async void HandleOpened(object? sender, EventArgs eventArgs) => await StartServerAsync();

    private async void HandleRetry(object? sender, RoutedEventArgs eventArgs) => await StartServerAsync();

    private async Task StartServerAsync()
    {
        StartupPanel.IsVisible = true;
        WebHost.IsVisible = false;
        StatusText.Text = "正在启动 DeepSeek Harness…";
        ErrorText.IsVisible = false;
        RetryButton.IsVisible = false;

        try
        {
            var url = await webHost.StartAsync(CancellationToken.None);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                WebHost.Content = new NativeWebView { Source = url };
                WebHost.IsVisible = true;
                StartupPanel.IsVisible = false;
            });
        }
        catch (Exception exception)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusText.Text = "DeepSeek Harness 启动失败。";
                ErrorText.Text = exception.Message;
                ErrorText.IsVisible = true;
                RetryButton.IsVisible = true;
            });
        }
    }

    private async void HandleClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        if (closing)
        {
            return;
        }

        eventArgs.Cancel = true;
        closing = true;
        await webHost.DisposeAsync();
        Close();
    }
}

namespace OrderWeb.Client.Services;

public static class ClientWindowService
{
    public static void ApplyLockedFullscreen(object? platformWindow)
    {
#if WINDOWS
        if (platformWindow is not Microsoft.UI.Xaml.Window window)
        {
            return;
        }

        var appWindow = GetAppWindow(window);
        if (appWindow?.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            return;
        }

        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = true;
        presenter.Maximize();

        window.Activated -= OnWindowActivated;
        window.Activated += OnWindowActivated;
#endif
    }

    public static void MinimizeMainWindow()
    {
#if WINDOWS
        var mauiWindow = Application.Current?.Windows.FirstOrDefault();
        if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window)
        {
            return;
        }

        var appWindow = GetAppWindow(window);
        if (appWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.Minimize();
        }
#endif
    }

#if WINDOWS
    private static Microsoft.UI.Windowing.AppWindow? GetAppWindow(Microsoft.UI.Xaml.Window window)
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
        return Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
    }

    private static void OnWindowActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs eventArgs)
    {
        if (eventArgs.WindowActivationState == Microsoft.UI.Xaml.WindowActivationState.Deactivated)
        {
            return;
        }

        if (sender is Microsoft.UI.Xaml.Window window)
        {
            ApplyLockedFullscreen(window);
        }
    }
#endif
}

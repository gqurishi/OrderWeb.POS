namespace POS_in_NET.Services;

public static class PosWindowService
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

        HideBuiltInShellMenuButton(window);
        _ = HideBuiltInShellMenuButtonWithRetriesAsync(window);
#endif
    }

    public static void MinimizeMainWindow()
    {
#if WINDOWS
        var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
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
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
    }

    private static void OnWindowActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == Microsoft.UI.Xaml.WindowActivationState.Deactivated)
        {
            return;
        }

        if (sender is Microsoft.UI.Xaml.Window window)
        {
            ApplyLockedFullscreen(window);
        }
    }

    private static async Task HideBuiltInShellMenuButtonWithRetriesAsync(Microsoft.UI.Xaml.Window window)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await Task.Delay(100 + attempt * 100);
            window.DispatcherQueue.TryEnqueue(() => HideBuiltInShellMenuButton(window));
        }
    }

    private static void HideBuiltInShellMenuButton(Microsoft.UI.Xaml.Window window)
    {
        if (window.Content is Microsoft.UI.Xaml.DependencyObject root)
        {
            HideBuiltInShellMenuButton(root);
        }
    }

    private static void HideBuiltInShellMenuButton(Microsoft.UI.Xaml.DependencyObject element)
    {
        if (element is Microsoft.UI.Xaml.FrameworkElement frameworkElement &&
            IsBuiltInShellMenuElement(frameworkElement))
        {
            frameworkElement.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;
            frameworkElement.IsHitTestVisible = false;
            frameworkElement.Opacity = 0;
            frameworkElement.Width = 0;
            frameworkElement.MinWidth = 0;
            frameworkElement.MaxWidth = 0;
            frameworkElement.Height = 0;
            frameworkElement.MinHeight = 0;
            frameworkElement.MaxHeight = 0;
        }

        var childCount = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(element);
        for (var index = 0; index < childCount; index++)
        {
            HideBuiltInShellMenuButton(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(element, index));
        }
    }

    private static bool IsBuiltInShellMenuElement(Microsoft.UI.Xaml.FrameworkElement element)
    {
        var name = element.Name ?? string.Empty;
        if (name.Equals("TogglePaneButton", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("PaneToggleButton", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("PaneToggleButtonGrid", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (element is not Microsoft.UI.Xaml.Controls.Button)
        {
            return false;
        }

        var automationName = Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(element) ?? string.Empty;
        return automationName.Contains("navigation", StringComparison.OrdinalIgnoreCase) &&
               (automationName.Contains("menu", StringComparison.OrdinalIgnoreCase) ||
                automationName.Contains("pane", StringComparison.OrdinalIgnoreCase));
    }
#endif
}

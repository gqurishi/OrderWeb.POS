using POS_in_NET.Views;

namespace POS_in_NET.Services;

/// <summary>
/// Pins the website-order print notice on the current Mother page so it stays
/// when staff leave a dashboard. Login and setup stay clear.
/// </summary>
public sealed class OnlineOrderPrintNoticePresenter
{
    private static readonly string[] HiddenRoutes = ["login", "terminalsetup", "initialadminsetup"];

    private readonly OnlineOrderPrintNoticeService _notice;
    private readonly OnlineOrderPrintNoticeView _view = new();
    private readonly Grid _host = new()
    {
        InputTransparent = true,
        CascadeInputTransparent = false,
        HorizontalOptions = LayoutOptions.Fill,
        VerticalOptions = LayoutOptions.Fill,
        ZIndex = 800
    };
    private bool _started;
    private bool _hooked;

    public OnlineOrderPrintNoticePresenter(OnlineOrderPrintNoticeService notice)
    {
        _notice = notice;
        _view.HorizontalOptions = LayoutOptions.End;
        _view.VerticalOptions = LayoutOptions.Start;
        _host.Children.Add(_view);
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _notice.Changed += (_, _) => MainThread.BeginInvokeOnMainThread(Apply);
        TryHookShell();
        _notice.Start();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Attach();
            Apply();
        });
    }

    private void TryHookShell()
    {
        if (_hooked || Shell.Current == null)
        {
            return;
        }

        Shell.Current.Navigated += (_, _) => MainThread.BeginInvokeOnMainThread(Apply);
        _hooked = true;
    }

    private void Apply()
    {
        TryHookShell();
        var snapshot = _notice.Snapshot;
        var loggedIn = AuthenticationService.Instance.CurrentUser != null;
        if (!loggedIn || !snapshot.IsVisible || IsHiddenRoute())
        {
            _view.IsVisible = false;
            return;
        }

        _view.Apply(snapshot.Text, snapshot.Tone);
        Attach();
    }

    private void Attach()
    {
        if (Shell.Current?.CurrentPage is not ContentPage page || IsHiddenRoute() || AuthenticationService.Instance.CurrentUser == null)
        {
            Detach();
            _view.IsVisible = false;
            return;
        }

        if (Contains(_host, page.Content))
        {
            return;
        }

        Detach();
        if (page.Content is Grid grid)
        {
            Place(grid);
            return;
        }

        var existing = page.Content;
        page.Content = null;
        var wrap = new Grid();
        if (existing != null)
        {
            wrap.Children.Add(existing);
        }

        Place(wrap);
        page.Content = wrap;
    }

    private void Place(Grid grid)
    {
        if (grid.RowDefinitions.Count > 0)
        {
            Grid.SetRow(_host, 0);
            Grid.SetRowSpan(_host, grid.RowDefinitions.Count);
        }

        if (grid.ColumnDefinitions.Count > 0)
        {
            Grid.SetColumn(_host, 0);
            Grid.SetColumnSpan(_host, grid.ColumnDefinitions.Count);
        }

        grid.Children.Add(_host);
    }

    private void Detach()
    {
        if (_host.Parent is Layout parent)
        {
            parent.Children.Remove(_host);
        }
    }

    private static bool Contains(View needle, View? root)
    {
        if (root == null)
        {
            return false;
        }

        if (ReferenceEquals(root, needle))
        {
            return true;
        }

        if (root is Layout layout)
        {
            return layout.Children.Any(child => child is View view && Contains(needle, view));
        }

        if (root is ContentView content)
        {
            return Contains(needle, content.Content as View);
        }

        return false;
    }

    private static bool IsHiddenRoute()
    {
        var location = Shell.Current?.CurrentState?.Location?.OriginalString ?? "";
        return HiddenRoutes.Any(route => location.Contains(route, StringComparison.OrdinalIgnoreCase));
    }
}

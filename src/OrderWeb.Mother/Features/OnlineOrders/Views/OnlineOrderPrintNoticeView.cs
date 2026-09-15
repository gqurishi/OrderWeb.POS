namespace POS_in_NET.Views;

public sealed class OnlineOrderPrintNoticeView : Border
{
    private readonly Label _label;

    public OnlineOrderPrintNoticeView()
    {
        Padding = new Thickness(14, 10);
        StrokeThickness = 1;
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 };
        HorizontalOptions = LayoutOptions.End;
        VerticalOptions = LayoutOptions.Start;
        Margin = new Thickness(0, 72, 18, 0);
        ZIndex = 800;
        IsVisible = false;
        Shadow = new Shadow
        {
            Brush = Colors.Black,
            Offset = new Point(0, 2),
            Radius = 8,
            Opacity = 0.16f
        };

        _label = new Label
        {
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center
        };
        Content = _label;

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        GestureRecognizers.Add(tap);
    }

    public void Apply(string text, Services.OnlineOrderPrintNoticeTone tone)
    {
        _label.Text = text;
        IsVisible = !string.IsNullOrWhiteSpace(text);
        InputTransparent = !IsVisible;
        (BackgroundColor, Stroke, _label.TextColor) = tone switch
        {
            Services.OnlineOrderPrintNoticeTone.Failed => (Color.FromArgb("#FEF2F2"), Color.FromArgb("#FECACA"), Color.FromArgb("#B91C1C")),
            Services.OnlineOrderPrintNoticeTone.Waiting => (Color.FromArgb("#FFFBEB"), Color.FromArgb("#FDE68A"), Color.FromArgb("#92400E")),
            _ => (Color.FromArgb("#EFF6FF"), Color.FromArgb("#BFDBFE"), Color.FromArgb("#1D4ED8"))
        };
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            var auth = Services.AuthenticationService.Instance;
            var access = Services.ServiceHelper.GetService<Services.RoleAccessService>();
            if (access != null && !access.CanAccessRoute(auth.CurrentUser?.Role, "weborders"))
            {
                return;
            }

            await Services.NavigationCoordinator.Shared.NavigateShellAsync("weborders", animated: false, source: this);
        }
        catch (Exception ex)
        {
            Services.AppDiagnostics.Log($"Online order notice could not open Web Orders: {ex.Message}");
        }
    }
}

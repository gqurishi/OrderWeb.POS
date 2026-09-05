using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Contracts.Customers;

namespace OrderWeb.SharedUI.Views;

/// <summary>Shared stale / offline / sync banner for customer and open-order screens.</summary>
public sealed class StaleDataBannerView : ContentView
{
    private readonly Border _root;
    private readonly Label _label;

    public StaleDataBannerView()
    {
        _label = new Label
        {
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center
        };
        _root = new Border
        {
            StrokeThickness = 0,
            Padding = new Thickness(14, 10),
            IsVisible = false,
            Content = _label
        };
        Content = _root;
    }

    public void Apply(CustomerSyncStatusDto? sync, string? bannerText = null, string? tone = null)
    {
        var text = !string.IsNullOrWhiteSpace(bannerText)
            ? bannerText
            : sync is { IsStale: true } or { IsOnline: false }
                ? sync.DisplayText
                : null;

        if (string.IsNullOrWhiteSpace(text))
        {
            _root.IsVisible = false;
            return;
        }

        _label.Text = text;
        _root.BackgroundColor = Color.FromArgb(ResolveTone(tone, sync));
        _root.IsVisible = true;
    }

    private static string ResolveTone(string? tone, CustomerSyncStatusDto? sync)
    {
        if (!string.IsNullOrWhiteSpace(tone))
        {
            if (tone.StartsWith('#'))
            {
                return tone;
            }

            return tone.Trim().ToLowerInvariant() switch
            {
                "error" or "danger" or "stale" => "#DC2626",
                "warning" or "offline" => "#F59E0B",
                "success" => "#10B981",
                "muted" or "permission" => "#64748B",
                "info" => "#3B82F6",
                _ => "#DC2626"
            };
        }

        return sync is { IsOnline: false } ? "#F59E0B" : "#DC2626";
    }
}

internal static class CustomerOrderUiHelpers
{
    public const string SuccessGreen = "#10B981";
    public const string StaleRed = "#DC2626";
    public const string DeliveryBlue = "#3B82F6";
    public const string PageBackground = "#F8FAFC";

    public static Border CreateTab(string text, bool selected, Color accent, EventHandler onClicked)
    {
        var label = new Label
        {
            Text = text,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = selected ? Colors.White : Color.FromArgb("#6B7280"),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };

        var border = new Border
        {
            BackgroundColor = selected ? accent : Color.FromArgb("#F5F5F5"),
            StrokeThickness = 0,
            Padding = new Thickness(24, 10),
            HeightRequest = 44,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = label
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onClicked(border, EventArgs.Empty);
        border.GestureRecognizers.Add(tap);
        return border;
    }

    public static View CreateField(string caption, View input) =>
        new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                new Label
                {
                    Text = caption,
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#475569")
                },
                new Border
                {
                    BackgroundColor = Colors.White,
                    Stroke = Color.FromArgb("#CBD5E1"),
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = 14 },
                    Content = input
                }
            }
        };

    public static Grid CreateLoadingOverlay(string message) =>
        new()
        {
            BackgroundColor = Color.FromRgba(255, 255, 255, 180),
            IsVisible = false,
            ZIndex = 900,
            Children =
            {
                new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center,
                    Spacing = 12,
                    Children =
                    {
                        new ActivityIndicator
                        {
                            IsRunning = true,
                            Color = Color.FromArgb(SuccessGreen)
                        },
                        new Label
                        {
                            Text = message,
                            FontSize = 14,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = Color.FromArgb("#475569"),
                            HorizontalTextAlignment = TextAlignment.Center
                        }
                    }
                }
            }
        };

    public static void SetLoadingMessage(Grid overlay, string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        if (overlay.Children.OfType<VerticalStackLayout>().FirstOrDefault()?
                .Children.OfType<Label>().FirstOrDefault() is { } label)
        {
            label.Text = message;
        }
    }

    public static Border CreateBadge(string text, string background, string foreground) =>
        new()
        {
            BackgroundColor = Color.FromArgb(background),
            StrokeThickness = 0,
            Padding = new Thickness(6, 4),
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Content = new Label
            {
                Text = text,
                FontSize = 9,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb(foreground),
                LineBreakMode = LineBreakMode.NoWrap
            }
        };

    public static CustomerSyncStatusDto LiveSync(string text = "Live") =>
        new(IsOnline: true, IsStale: false, LastSyncedAtUtc: DateTimeOffset.UtcNow, DisplayText: text);

    public static CustomerSyncStatusDto OfflineSync(string text = "Offline — showing cached data") =>
        new(IsOnline: false, IsStale: true, LastSyncedAtUtc: null, DisplayText: text);
}

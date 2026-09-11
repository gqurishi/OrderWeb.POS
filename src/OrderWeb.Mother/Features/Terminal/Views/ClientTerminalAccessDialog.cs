using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Contracts.Access;
using POS_in_NET.Services;

namespace POS_in_NET.Views;

public sealed class ClientTerminalAccessDialog : ContentView
{
    private readonly Dictionary<string, Switch> _switches = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource<IReadOnlyList<string>?>? _completion;
    private Grid? _parentGrid;

    public ClientTerminalAccessDialog()
    {
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        ZIndex = 1000;
        BackgroundColor = Color.FromArgb("#990F172A");

        var rows = new VerticalStackLayout { Spacing = 10 };
        foreach (var (key, label) in ClientAccessPolicy.EditableFeatures)
        {
            var toggle = new Switch
            {
                OnColor = Color.FromArgb("#059669"),
                ThumbColor = Colors.White
            };
            _switches[key] = toggle;

            var note = string.Equals(key, OrderWeb.Contracts.Features.PosFeatureKeys.Reservations, StringComparison.OrdinalIgnoreCase)
                ? "On by default so Client matches Mother. Turn off to hide on this Client."
                : null;

            var labels = new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = label,
                        FontFamily = "OpenSansSemibold",
                        FontSize = 14,
                        TextColor = Color.FromArgb("#0F172A")
                    }
                }
            };
            if (!string.IsNullOrWhiteSpace(note))
            {
                labels.Children.Add(new Label
                {
                    Text = note,
                    FontFamily = "OpenSansRegular",
                    FontSize = 11,
                    TextColor = Color.FromArgb("#64748B")
                });
            }

            rows.Children.Add(new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new(GridLength.Star),
                    new(GridLength.Auto)
                },
                Children =
                {
                    labels,
                    toggle
                }
            });
            Grid.SetColumn(toggle, 1);
        }

        var save = new Button
        {
            Text = "Save access",
            BackgroundColor = Color.FromArgb("#1D4ED8"),
            TextColor = Colors.White,
            FontFamily = "OpenSansSemibold",
            CornerRadius = 8,
            Padding = new Thickness(16, 10)
        };
        save.Clicked += (_, _) => Complete(CollectEnabled());

        var cancel = new Button
        {
            Text = "Cancel",
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            TextColor = Color.FromArgb("#334155"),
            BorderColor = Color.FromArgb("#CBD5E1"),
            BorderWidth = 1,
            FontFamily = "OpenSansSemibold",
            CornerRadius = 8,
            Padding = new Thickness(16, 10)
        };
        cancel.Clicked += (_, _) => Complete(null);

        var card = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = 24,
            WidthRequest = 460,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children =
                {
                    new Label
                    {
                        Text = "Client access",
                        FontFamily = "OpenSansSemibold",
                        FontSize = 20,
                        TextColor = Color.FromArgb("#0F172A")
                    },
                    new Label
                    {
                        Text = "Mother decides what this Client can see and do. Web Orders stay on Mother. Client picks this up on Update All, Gift Cards/Loyalty open, or the next PIN login.",
                        FontFamily = "OpenSansRegular",
                        FontSize = 13,
                        TextColor = Color.FromArgb("#64748B")
                    },
                    new ScrollView
                    {
                        MaximumHeightRequest = 420,
                        Content = rows
                    },
                    new HorizontalStackLayout
                    {
                        Spacing = 8,
                        HorizontalOptions = LayoutOptions.End,
                        Children = { cancel, save }
                    }
                }
            }
        };

        Content = card;
    }

    public void SetGrantedFeatures(IReadOnlySet<string> granted)
    {
        foreach (var (key, toggle) in _switches)
        {
            toggle.IsToggled = granted.Contains(key);
        }
    }

    public async Task<IReadOnlyList<string>?> ShowAsync()
    {
        _completion = new TaskCompletionSource<IReadOnlyList<string>?>();
        if (!DialogOverlayHelper.TryAttachOverlay(this, out _parentGrid))
        {
            return null;
        }

        return await _completion.Task;
    }

    private IReadOnlyList<string> CollectEnabled() =>
        _switches.Where(pair => pair.Value.IsToggled).Select(pair => pair.Key).ToList();

    private void Complete(IReadOnlyList<string>? result)
    {
        if (_completion is null)
        {
            return;
        }

        DialogOverlayHelper.DetachOverlay(this, _parentGrid);
        _parentGrid = null;
        _completion.TrySetResult(result);
        _completion = null;
    }
}

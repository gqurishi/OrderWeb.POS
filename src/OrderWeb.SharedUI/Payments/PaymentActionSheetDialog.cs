using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Payments;

/// <summary>Mother-chrome action sheet (Payment Setup / Split Evenly).</summary>
public sealed class PaymentActionSheetDialog : ContentView
{
    private readonly Border _iconBorder;
    private readonly Label _iconLabel;
    private readonly Label _titleLabel;
    private readonly Label _subtitleLabel;
    private readonly VerticalStackLayout _listOptions;
    private readonly Grid _gridOptions;
    private readonly Button _cancelButton;
    private readonly Border _dialogBorder;
    private TaskCompletionSource<string?>? _tcs;
    private Grid? _parent;

    public PaymentActionSheetDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;

        _iconLabel = new Label
        {
            Text = "£",
            TextColor = Colors.White,
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        _iconBorder = new Border
        {
            WidthRequest = 64,
            HeightRequest = 64,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 32 },
            BackgroundColor = Color.FromArgb("#059669"),
            HorizontalOptions = LayoutOptions.Center,
            Content = _iconLabel
        };

        _titleLabel = new Label
        {
            Text = "Payment Setup",
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#0F172A"),
            HorizontalTextAlignment = TextAlignment.Center
        };

        _subtitleLabel = new Label
        {
            Text = string.Empty,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#475569"),
            HorizontalTextAlignment = TextAlignment.Center,
            IsVisible = false
        };

        _listOptions = new VerticalStackLayout { Spacing = 10, IsVisible = false };
        _gridOptions = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) },
            ColumnSpacing = 12,
            RowSpacing = 12,
            IsVisible = false
        };

        _cancelButton = new Button
        {
            Text = "Back",
            BackgroundColor = Color.FromArgb("#E2E8F0"),
            TextColor = Color.FromArgb("#1E293B"),
            FontAttributes = FontAttributes.Bold,
            FontSize = 16,
            CornerRadius = 14,
            HeightRequest = 50
        };
        _cancelButton.Clicked += (_, _) => Complete(null);

        _dialogBorder = new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Color.FromArgb("#E2E8F0"),
            StrokeThickness = 1,
            Padding = 28,
            WidthRequest = 520,
            MaximumWidthRequest = 560,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Content = new VerticalStackLayout
            {
                Spacing = 18,
                Children = { _iconBorder, _titleLabel, _subtitleLabel, _listOptions, _gridOptions, _cancelButton }
            }
        };

        Content = new Grid { Padding = 24, Children = { _dialogBorder } };
    }

    public void SetSubtitle(string? text)
    {
        var value = text?.Trim() ?? string.Empty;
        _subtitleLabel.Text = value;
        _subtitleLabel.IsVisible = !string.IsNullOrEmpty(value);
    }

    public void SetActionSheet(string title, IReadOnlyList<string> options, string icon = "i", string iconBg = "#2563EB")
    {
        _dialogBorder.WidthRequest = 450;
        ConfigureHeader(title, icon, iconBg);
        SetSubtitle(null);
        _listOptions.Children.Clear();
        _listOptions.IsVisible = true;
        _gridOptions.IsVisible = false;
        _gridOptions.Children.Clear();
        _gridOptions.RowDefinitions.Clear();

        foreach (var option in options)
        {
            _listOptions.Children.Add(MakeOptionButton(option, option, null, 50, 14));
        }
    }

    public void SetActionSheetGrid(string title, IReadOnlyList<string> options, string icon = "£", string iconBg = "#059669")
    {
        var mapped = options.Select(option => (Key: option, Title: option, Detail: (string?)null)).ToList();
        SetActionSheetGrid(title, mapped, icon, iconBg);
    }

    /// <summary>Grid options with optional detail line (e.g. "£12.50 each").</summary>
    public void SetActionSheetGrid(
        string title,
        IReadOnlyList<(string Key, string Title, string? Detail)> options,
        string icon = "£",
        string iconBg = "#059669")
    {
        _dialogBorder.WidthRequest = 520;
        ConfigureHeader(title, icon, iconBg);
        _listOptions.IsVisible = false;
        _listOptions.Children.Clear();
        _gridOptions.IsVisible = true;
        _gridOptions.Children.Clear();
        _gridOptions.RowDefinitions.Clear();

        var rows = (int)Math.Ceiling(options.Count / 2d);
        for (var r = 0; r < rows; r++)
        {
            _gridOptions.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            var height = string.IsNullOrWhiteSpace(option.Detail) ? 72d : 84d;
            _gridOptions.Add(
                MakeOptionButton(option.Key, option.Title, option.Detail, height, 16),
                i % 2,
                i / 2);
        }
    }

    public void SetCancelText(string text) => _cancelButton.Text = text;

    public void HighlightGridOption(string optionText, string background, string text, string border)
    {
        foreach (var child in _gridOptions.Children)
        {
            if (child is Border card &&
                string.Equals(card.StyleId, optionText, StringComparison.OrdinalIgnoreCase))
            {
                card.BackgroundColor = Color.FromArgb(background);
                card.Stroke = Color.FromArgb(border);
                if (card.Content is VerticalStackLayout stack)
                {
                    foreach (var line in stack.Children)
                    {
                        if (line is Label label)
                        {
                            label.TextColor = Color.FromArgb(text);
                        }
                    }
                }
            }
            else if (child is Button button &&
                     string.Equals(button.Text, optionText, StringComparison.OrdinalIgnoreCase))
            {
                button.BackgroundColor = Color.FromArgb(background);
                button.TextColor = Color.FromArgb(text);
                button.BorderColor = Color.FromArgb(border);
            }
        }
    }

    public async Task<string?> ShowAsync(ContentPage? hostPage = null)
    {
        _tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _parent = PaymentOverlayHost.Attach(this, hostPage);
        if (_parent is null)
        {
            return null;
        }

        return await _tcs.Task;
    }

    private void ConfigureHeader(string title, string icon, string iconBg)
    {
        _titleLabel.Text = title;
        _iconLabel.Text = string.IsNullOrWhiteSpace(icon) ? "£" : icon.Trim()[..Math.Min(2, icon.Trim().Length)];
        _iconBorder.BackgroundColor = Color.FromArgb(iconBg);
    }

    private View MakeOptionButton(string key, string title, string? detail, double height, double fontSize)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            var button = new Button
            {
                Text = title,
                StyleId = key,
                BackgroundColor = Color.FromArgb("#F8FAFC"),
                TextColor = Color.FromArgb("#1E293B"),
                FontSize = fontSize,
                FontAttributes = FontAttributes.Bold,
                CornerRadius = 16,
                HeightRequest = height,
                BorderColor = Color.FromArgb("#D8E1ED"),
                BorderWidth = 1
            };
            button.Clicked += (_, _) => Complete(key);
            return button;
        }

        var titleLabel = new Label
        {
            Text = title,
            FontSize = fontSize,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#0F172A"),
            HorizontalTextAlignment = TextAlignment.Center
        };
        var detailLabel = new Label
        {
            Text = detail,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#2563EB"),
            HorizontalTextAlignment = TextAlignment.Center
        };

        var card = new Border
        {
            StyleId = key,
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            Stroke = Color.FromArgb("#D8E1ED"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Padding = new Thickness(10, 12),
            HeightRequest = height,
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                VerticalOptions = LayoutOptions.Center,
                Children = { titleLabel, detailLabel }
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Complete(key);
        card.GestureRecognizers.Add(tap);
        return card;
    }

    private void Complete(string? result)
    {
        var parent = _parent;
        _parent = null;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PaymentOverlayHost.Detach(this, parent);
            _tcs?.TrySetResult(result);
        });
    }
}

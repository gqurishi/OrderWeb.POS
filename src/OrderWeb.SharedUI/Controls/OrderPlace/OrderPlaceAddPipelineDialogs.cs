using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Controls.OrderPlace;

/// <summary>Host-neutral options for Mother-parity add pipeline (variant → note → addons).</summary>
public sealed record OrderPlaceVariantChoice(
    string Id,
    string Name,
    string? Description,
    decimal Price);

public sealed record OrderPlaceAddonChoice(
    string Id,
    string Name,
    decimal Price);

public enum OrderPlaceQuickNoteKind
{
    Cancelled,
    NoNote,
    SavedNote,
    CustomNote
}

public sealed record OrderPlaceQuickNoteResult(OrderPlaceQuickNoteKind Kind, string? NoteText = null);

/// <summary>Attaches a full-page overlay dialog to a page's root grid (Mother DialogOverlayHelper parity).</summary>
public static class OrderPlaceDialogPresenter
{
    public static async Task<T> ShowAsync<T>(ContentPage page, ContentView dialog, TaskCompletionSource<T> completion)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(dialog);
        ArgumentNullException.ThrowIfNull(completion);

        Grid host;
        if (page.Content is Grid existing)
        {
            host = existing;
        }
        else
        {
            host = new Grid();
            if (page.Content != null)
            {
                host.Children.Add(page.Content);
            }

            page.Content = host;
        }

        dialog.HorizontalOptions = LayoutOptions.Fill;
        dialog.VerticalOptions = LayoutOptions.Fill;
        host.Children.Add(dialog);

        try
        {
            return await completion.Task.ConfigureAwait(true);
        }
        finally
        {
            host.Children.Remove(dialog);
        }
    }
}

/// <summary>Mother-style single-select variant / size picker.</summary>
public sealed class OrderPlaceVariantDialog : ContentView
{
    private readonly VerticalStackLayout _rows = new() { Spacing = 8 };
    private readonly Label _title = new() { FontAttributes = FontAttributes.Bold, FontSize = 20 };
    private readonly Label _subtitle = new() { FontSize = 14 };
    private TaskCompletionSource<OrderPlaceVariantChoice?>? _tcs;

    public OrderPlaceVariantDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        _title.Use(Label.TextColorProperty, "OwTextStrong");
        _subtitle.Use(Label.TextColorProperty, "OwTextMuted");

        var cancel = new SharedButton { Text = "Cancel", Variant = ButtonVariant.Secondary };
        cancel.Clicked += (_, _) => Complete(null);

        var body = new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                _title,
                _subtitle,
                new ScrollView { MaximumHeightRequest = 360, Content = _rows },
                cancel
            }
        };

        var panel = new Border
        {
            Padding = 22,
            WidthRequest = 520,
            MaximumWidthRequest = 620,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Content = body,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        panel.Use(Border.BackgroundColorProperty, "OwSurface");
        panel.Use(Border.StrokeProperty, "OwBorder");
        Content = new Grid { Padding = 18, Children = { panel } };
    }

    public Task<OrderPlaceVariantChoice?> ShowAsync(
        ContentPage page,
        string itemName,
        IEnumerable<OrderPlaceVariantChoice> variants)
    {
        _tcs = new TaskCompletionSource<OrderPlaceVariantChoice?>();
        var list = variants
            .Where(v => !string.IsNullOrWhiteSpace(v.Name))
            .ToList();

        _title.Text = itemName;
        _subtitle.Text = list.Count == 1
            ? "Select the available size before adding this item."
            : "Select one size or portion before adding this item.";

        _rows.Children.Clear();
        foreach (var variant in list)
        {
            _rows.Children.Add(BuildVariantRow(variant));
        }

        return OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs);
    }

    private View BuildVariantRow(OrderPlaceVariantChoice variant)
    {
        var row = new Border
        {
            Padding = new Thickness(14, 12),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 }
        };
        row.Use(Border.BackgroundColorProperty, "OwSurface");
        row.Use(Border.StrokeProperty, "OwBorder");

        var marker = new Border
        {
            WidthRequest = 30,
            HeightRequest = 30,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 15 },
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "£",
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        marker.Use(Border.BackgroundColorProperty, "OwPrimarySoft");
        marker.Use(Border.StrokeProperty, "OwPrimary");
        if (marker.Content is Label markerLabel)
        {
            markerLabel.Use(Label.TextColorProperty, "OwPrimary");
        }

        var text = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        var name = new Label
        {
            Text = variant.Name,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation
        };
        name.Use(Label.TextColorProperty, "OwTextStrong");
        text.Children.Add(name);
        if (!string.IsNullOrWhiteSpace(variant.Description))
        {
            var detail = new Label
            {
                Text = variant.Description.Trim(),
                FontSize = 13,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            detail.Use(Label.TextColorProperty, "OwTextMuted");
            text.Children.Add(detail);
        }

        var price = new Label
        {
            Text = $"£{variant.Price:F2}",
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center
        };
        price.Use(Label.TextColorProperty, "OwSuccessStrong");

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            Children = { marker }
        };
        grid.Add(text, 1);
        grid.Add(price, 2);
        row.Content = grid;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Complete(variant);
        row.GestureRecognizers.Add(tap);
        return row;
    }

    private void Complete(OrderPlaceVariantChoice? value) => _tcs?.TrySetResult(value);
}

/// <summary>Mother-style quick-note picker (saved / custom / no note / cancel).</summary>
public sealed class OrderPlaceQuickNoteDialog : ContentView
{
    private readonly VerticalStackLayout _rows = new() { Spacing = 8 };
    private readonly Label _title = new() { FontAttributes = FontAttributes.Bold, FontSize = 20 };
    private TaskCompletionSource<OrderPlaceQuickNoteResult>? _tcs;

    public OrderPlaceQuickNoteDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        _title.Use(Label.TextColorProperty, "OwTextStrong");

        var noNote = new SharedButton { Text = "No Note", Variant = ButtonVariant.Secondary };
        noNote.Clicked += (_, _) => Complete(new OrderPlaceQuickNoteResult(OrderPlaceQuickNoteKind.NoNote));
        var cancel = new SharedButton { Text = "Cancel", Variant = ButtonVariant.Secondary };
        cancel.Clicked += (_, _) => Complete(new OrderPlaceQuickNoteResult(OrderPlaceQuickNoteKind.Cancelled));

        var buttons = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10
        };
        buttons.Add(cancel);
        buttons.Add(noNote, 1);

        var subtitle = new Label { Text = "Choose a kitchen note for this item.", FontSize = 14 };
        subtitle.Use(Label.TextColorProperty, "OwTextMuted");

        var body = new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                _title,
                subtitle,
                new ScrollView { MaximumHeightRequest = 360, Content = _rows },
                buttons
            }
        };

        var panel = new Border
        {
            Padding = 22,
            WidthRequest = 520,
            MaximumWidthRequest = 620,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Content = body,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        panel.Use(Border.BackgroundColorProperty, "OwSurface");
        panel.Use(Border.StrokeProperty, "OwBorder");
        Content = new Grid { Padding = 18, Children = { panel } };
    }

    public Task<OrderPlaceQuickNoteResult> ShowAsync(ContentPage page, string itemName, IEnumerable<string> notes)
    {
        _tcs = new TaskCompletionSource<OrderPlaceQuickNoteResult>();
        _title.Text = itemName;
        _rows.Children.Clear();

        foreach (var note in notes.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            var text = note.Trim();
            _rows.Children.Add(BuildRow(text, () => Complete(new OrderPlaceQuickNoteResult(OrderPlaceQuickNoteKind.SavedNote, text))));
        }

        _rows.Children.Add(BuildRow("Custom note...", () => Complete(new OrderPlaceQuickNoteResult(OrderPlaceQuickNoteKind.CustomNote))));

        return OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs);
    }

    private static View BuildRow(string text, Action onTap)
    {
        var row = new Border
        {
            Padding = new Thickness(14, 12),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 }
        };
        row.Use(Border.BackgroundColorProperty, "OwSurface");
        row.Use(Border.StrokeProperty, "OwBorder");

        var label = new Label
        {
            Text = text,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap,
            VerticalOptions = LayoutOptions.Center
        };
        label.Use(Label.TextColorProperty, "OwTextStrong");
        row.Content = label;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onTap();
        row.GestureRecognizers.Add(tap);
        return row;
    }

    private void Complete(OrderPlaceQuickNoteResult value) => _tcs?.TrySetResult(value);
}

/// <summary>Mother-style multi-select add-ons picker.</summary>
public sealed class OrderPlaceAddonDialog : ContentView
{
    private readonly VerticalStackLayout _rows = new() { Spacing = 8 };
    private readonly Label _title = new() { FontAttributes = FontAttributes.Bold, FontSize = 20 };
    private readonly Label _subtitle = new() { FontSize = 14 };
    private readonly Label _summary = new() { FontSize = 14, IsVisible = false };
    private readonly SharedButton _done = new() { Text = "No Add-ons", Variant = ButtonVariant.Primary };
    private readonly List<OrderPlaceAddonChoice> _addons = new();
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _borders = new(StringComparer.OrdinalIgnoreCase);
    private TaskCompletionSource<IReadOnlyList<OrderPlaceAddonChoice>?>? _tcs;

    public OrderPlaceAddonDialog()
    {
        BackgroundColor = Color.FromArgb("#80000000");
        _title.Use(Label.TextColorProperty, "OwTextStrong");
        _subtitle.Use(Label.TextColorProperty, "OwTextMuted");
        _summary.Use(Label.TextColorProperty, "OwPrimary");

        _done.Clicked += (_, _) =>
        {
            var selected = _addons.Where(a => _selected.Contains(a.Id)).ToList();
            _tcs?.TrySetResult(selected);
        };

        var cancel = new SharedButton { Text = "Cancel", Variant = ButtonVariant.Secondary };
        cancel.Clicked += (_, _) => _tcs?.TrySetResult(null);

        var buttons = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 10
        };
        buttons.Add(cancel);
        buttons.Add(_done, 1);

        var body = new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                _title,
                _subtitle,
                new ScrollView { MaximumHeightRequest = 360, Content = _rows },
                _summary,
                buttons
            }
        };

        var panel = new Border
        {
            Padding = 22,
            WidthRequest = 520,
            MaximumWidthRequest = 620,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 22 },
            Content = body,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        panel.Use(Border.BackgroundColorProperty, "OwSurface");
        panel.Use(Border.StrokeProperty, "OwBorder");
        Content = new Grid { Padding = 18, Children = { panel } };
    }

    public Task<IReadOnlyList<OrderPlaceAddonChoice>?> ShowAsync(
        ContentPage page,
        string itemName,
        IEnumerable<OrderPlaceAddonChoice> addons)
    {
        _tcs = new TaskCompletionSource<IReadOnlyList<OrderPlaceAddonChoice>?>();
        _addons.Clear();
        _addons.AddRange(addons.Where(a => !string.IsNullOrWhiteSpace(a.Name)));
        _selected.Clear();
        _borders.Clear();
        _rows.Children.Clear();

        _title.Text = $"Add-ons for {itemName}";
        _subtitle.Text = _addons.Count == 1
            ? "Select an optional extra for this item."
            : "Select one or more optional extras for this item.";

        foreach (var addon in _addons)
        {
            _rows.Children.Add(BuildAddonRow(addon));
        }

        RefreshState();
        return OrderPlaceDialogPresenter.ShowAsync(page, this, _tcs);
    }

    private View BuildAddonRow(OrderPlaceAddonChoice addon)
    {
        var row = new Border
        {
            Padding = new Thickness(14, 12),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 }
        };
        row.Use(Border.BackgroundColorProperty, "OwSurface");
        row.Use(Border.StrokeProperty, "OwBorder");

        var check = new Label
        {
            Text = string.Empty,
            WidthRequest = 28,
            HeightRequest = 28,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#CBD5E1"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center
        };

        var name = new Label
        {
            Text = addon.Name,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center
        };
        name.Use(Label.TextColorProperty, "OwTextStrong");

        var price = new Label
        {
            Text = $"+£{addon.Price:F2}",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center
        };
        price.Use(Label.TextColorProperty, "OwSuccessStrong");

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        grid.Add(check);
        grid.Add(name, 1);
        grid.Add(price, 2);
        row.Content = grid;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (!_selected.Add(addon.Id))
            {
                _selected.Remove(addon.Id);
            }

            RefreshState();
        };
        row.GestureRecognizers.Add(tap);
        _borders[addon.Id] = row;
        return row;
    }

    private void RefreshState()
    {
        foreach (var addon in _addons)
        {
            if (!_borders.TryGetValue(addon.Id, out var row) || row.Content is not Grid grid)
            {
                continue;
            }

            var selected = _selected.Contains(addon.Id);
            row.StrokeThickness = selected ? 2 : 1;
            if (selected)
            {
                row.Use(Border.BackgroundColorProperty, "OwPrimarySoft");
                row.Use(Border.StrokeProperty, "OwPrimary");
            }
            else
            {
                row.Use(Border.BackgroundColorProperty, "OwSurface");
                row.Use(Border.StrokeProperty, "OwBorder");
            }

            if (grid.Children.FirstOrDefault() is Label check)
            {
                check.Text = selected ? "✓" : string.Empty;
                check.BackgroundColor = selected ? Color.FromArgb("#2563EB") : Color.FromArgb("#CBD5E1");
            }
        }

        var count = _selected.Count;
        var total = _addons.Where(a => _selected.Contains(a.Id)).Sum(a => a.Price);
        _done.Text = count == 0 ? "No Add-ons" : "Add Selected";
        _summary.IsVisible = count > 0;
        _summary.Text = $"{count} selected · +£{total:F2}";
    }
}

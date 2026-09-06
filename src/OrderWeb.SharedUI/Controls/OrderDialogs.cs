using Microsoft.Maui.Controls.Shapes;

namespace OrderWeb.SharedUI.Controls;

/// <summary>Host-neutral option used by the shared Mother-style order dialogs.</summary>
public sealed record OrderDialogOption(string Id, string Title, string? Detail = null, decimal? Price = null, bool IsSelected = false, bool IsEnabled = true);

public abstract class OrderDialogBase : ContentView
{
    protected readonly VerticalStackLayout Body = new() { Spacing = 12 };
    protected readonly SharedButton ConfirmButton = new() { Text = "Confirm", Variant = ButtonVariant.Primary };
    protected readonly SharedButton CancelButton = new() { Text = "Cancel", Variant = ButtonVariant.Secondary };
    protected OrderDialogBase(string icon, string title, string message, string accentKey = "OwPrimary")
    {
        BackgroundColor = Color.FromArgb("#80000000");
        HorizontalOptions = LayoutOptions.Fill; VerticalOptions = LayoutOptions.Fill;
        var iconLabel = new Label { Text = icon, FontSize = 28, FontAttributes = FontAttributes.Bold, TextColor = Colors.White, HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center };
        var iconShell = new Border { WidthRequest = 58, HeightRequest = 58, StrokeThickness = 0, StrokeShape = new RoundRectangle { CornerRadius = 29 }, Content = iconLabel, HorizontalOptions = LayoutOptions.Center };
        iconShell.Use(Border.BackgroundColorProperty, accentKey);
        var heading = new Label { Text = title, FontSize = 23, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center }; heading.Use(Label.TextColorProperty, "OwTextStrong");
        var subheading = new Label { Text = message, FontSize = 14, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap }; subheading.Use(Label.TextColorProperty, "OwTextMuted");
        var buttons = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10 }; buttons.Add(CancelButton); buttons.Add(ConfirmButton, 1);
        ConfirmButton.Clicked += (_, _) => Confirmed?.Invoke(this, EventArgs.Empty);
        CancelButton.Clicked += (_, _) => Cancelled?.Invoke(this, EventArgs.Empty);
        var content = new VerticalStackLayout { Spacing = 14, Children = { iconShell, heading, subheading, Body, buttons } };
        var panel = new Border { Padding = 22, WidthRequest = 520, MaximumWidthRequest = 620, StrokeThickness = 1, StrokeShape = new RoundRectangle { CornerRadius = 22 }, Content = content, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        panel.Use(Border.BackgroundColorProperty, "OwSurface"); panel.Use(Border.StrokeProperty, "OwBorder");
        Content = new Grid { Padding = 18, Children = { panel } };
    }
    public event EventHandler? Confirmed;
    public event EventHandler? Cancelled;
}

public sealed class OrderNoteDialog : OrderDialogBase
{
    private readonly Editor _note;
    private readonly Picker _category;
    private readonly Picker _priority;
    public OrderNoteDialog() : base("+", "Add Note", "Add a cooking instruction, allergy, or special request.", "OwWarning")
    {
        _note = new Editor { Placeholder = "e.g. No onions, extra spicy, allergy", AutoSize = EditorAutoSizeOption.TextChanges, HeightRequest = 100 };
        _note.Use(Editor.TextColorProperty, "OwTextPrimary");
        _category = new Picker { Title = "Category", ItemsSource = new[] { "Cooking", "Allergy", "Special request", "Dietary", "Other" } };
        _priority = new Picker { Title = "Priority", SelectedIndex = 1, ItemsSource = new[] { "Low", "Normal", "High", "Urgent" } };
        Body.Children.Add(new Label { Text = "Note" }); Body.Children.Add(_note); Body.Children.Add(_category); Body.Children.Add(_priority);
    }
    public string Note { get => _note.Text ?? string.Empty; set => _note.Text = value; }
    public string? Category => _category.SelectedItem?.ToString();
    public string? Priority => _priority.SelectedItem?.ToString();
}

public class OrderOptionSelectionDialog : OrderDialogBase
{
    private readonly VerticalStackLayout _options = new() { Spacing = 8 };
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    public OrderOptionSelectionDialog(string title = "Choose Options", string message = "Select one or more options.", bool allowMultiple = true) : base("+", title, message, "OwPrimary") { AllowMultiple = allowMultiple; Body.Children.Add(new ScrollView { MaximumHeightRequest = 360, Content = _options }); }
    public bool AllowMultiple { get; }
    public IReadOnlyCollection<string> SelectedIds => _selected;
    public void SetOptions(IEnumerable<OrderDialogOption> options)
    {
        _options.Children.Clear(); _selected.Clear();
        foreach (var option in options)
        {
            var detail = string.IsNullOrWhiteSpace(option.Detail) ? string.Empty : $"\n{option.Detail}";
            var price = option.Price is null ? string.Empty : $"  +{option.Price.Value:C2}";
            var button = new SharedButton { Text = option.Title + price + detail, Variant = option.IsSelected ? ButtonVariant.Primary : ButtonVariant.Secondary, IsEnabled = option.IsEnabled };
            if (option.IsSelected) _selected.Add(option.Id);
            button.Clicked += (_, _) => { if (!AllowMultiple) _selected.Clear(); if (!_selected.Add(option.Id)) _selected.Remove(option.Id); button.Variant = _selected.Contains(option.Id) ? ButtonVariant.Primary : ButtonVariant.Secondary; };
            _options.Children.Add(button);
        }
    }
}

public sealed class OrderQuantityDialog : OrderDialogBase
{
    private readonly Label _quantity;
    private int _value = 1;
    public OrderQuantityDialog() : base("#", "Quantity", "Choose the quantity for this line.")
    {
        _quantity = new Label { FontSize = 34, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center }; _quantity.Use(Label.TextColorProperty, "OwPrimary");
        var keypad = new NumberKeypad { KeySize = 58, ShowActions = false }; keypad.KeyPressed += (_, e) => { if (int.TryParse(e.Key, out var digit)) Quantity = Math.Min(999, Quantity * 10 + digit); };
        Body.Children.Add(_quantity); Body.Children.Add(keypad); Quantity = 1;
    }
    public int Quantity { get => _value; set { _value = Math.Max(1, value); _quantity.Text = _value.ToString(); } }
}

public sealed class OrderDiscountDialog : OrderDialogBase
{
    private readonly Entry _amount;
    private readonly Picker _type;
    public OrderDiscountDialog() : base("%", "Apply Discount", "Mother validates the final discount and total.", "OwPrimary")
    {
        _type = new Picker { ItemsSource = new[] { "Fixed amount", "Percentage" }, SelectedIndex = 0 };
        _amount = new Entry { Keyboard = Keyboard.Numeric, Placeholder = "0.00" };
        Body.Children.Add(_type); Body.Children.Add(_amount);
    }
    public bool IsPercentage => _type.SelectedIndex == 1;
    public decimal Amount => decimal.TryParse(_amount.Text, out var amount) ? amount : 0m;
}

public sealed class ManagerApprovalDialog : OrderDialogBase
{
    private readonly Entry _pin = new() { Placeholder = "Manager PIN", IsPassword = true, Keyboard = Keyboard.Numeric };
    public ManagerApprovalDialog() : base("!", "Manager Approval", "A manager must approve this action.", "OwWarning") { Body.Children.Add(_pin); }
    public string Pin => _pin.Text ?? string.Empty;
}

public sealed class VoidOrderConfirmationDialog : OrderDialogBase
{
    private readonly Editor _reason = new() { Placeholder = "Reason for void", HeightRequest = 76 };
    public VoidOrderConfirmationDialog() : base("!", "Void Order", "This action must be approved by Mother POS.", "OwError") { ConfirmButton.Text = "Void"; ConfirmButton.Variant = ButtonVariant.Danger; Body.Children.Add(_reason); }
    public string Reason => _reason.Text ?? string.Empty;
}

public sealed class PreviousOrdersDialog : OrderOptionSelectionDialog { public PreviousOrdersDialog() : base("Previous Orders", "Choose an order to reopen.", false) { } }
public sealed class MoreOrderOptionsDialog : OrderOptionSelectionDialog { public MoreOrderOptionsDialog() : base("More Options", "Choose an action for this order.", false) { } }
public sealed class TastingMenuConfirmationDialog : OrderOptionSelectionDialog { public TastingMenuConfirmationDialog() : base("Add Tasting Menu", "Review the menu courses before adding them.", true) { } }
public sealed class RefundConfirmationDialog : OrderDialogBase { public RefundConfirmationDialog() : base("£", "Confirm Refund", "Mother POS must approve and process this refund.", "OwError") { ConfirmButton.Text = "Request refund"; ConfirmButton.Variant = ButtonVariant.Danger; } }

public sealed class TastingCourseProgressDialog : OrderDialogBase
{
    private readonly Label _progress;
    public TastingCourseProgressDialog() : base("T", "Tasting Menu Progress", "Track course progress for this table.", "OwInfo") { _progress = new Label { HorizontalTextAlignment = TextAlignment.Center, FontSize = 18 }; _progress.Use(Label.TextColorProperty, "OwTextStrong"); Body.Children.Add(_progress); }
    public void SetProgress(int completed, int total) => _progress.Text = $"Course {Math.Min(completed + 1, total)} of {total}";
}

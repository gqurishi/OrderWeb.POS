using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.ViewModels;

namespace OrderWeb.SharedUI.Views;

/// <summary>Canonical Mother-style payment UI. It cannot declare payment success itself.</summary>
public sealed class PaymentView : ContentView
{
    private readonly Label _due;
    private readonly Entry _tendered;
    private readonly Label _change;
    private readonly Label _remaining;
    private readonly Label _status;
    private readonly ActivityIndicator _waiting;
    private readonly SharedButton _submit;
    private readonly Grid _methods;
    private PaymentViewModel? _viewModel;
    public PaymentView()
    {
        _due = Money(36, true); _tendered = new Entry { Keyboard = Keyboard.Numeric, FontSize = 20 }; _tendered.Use(Entry.TextColorProperty, "OwTextPrimary");
        _change = Money(18, true); _remaining = Money(18, true); _status = new Microsoft.Maui.Controls.Label { FontSize = 14, HorizontalTextAlignment = TextAlignment.Center, LineBreakMode = LineBreakMode.WordWrap }; _status.Use(Microsoft.Maui.Controls.Label.TextColorProperty, "OwTextMuted");
        _waiting = new ActivityIndicator { IsVisible = false, IsRunning = false, WidthRequest = 36, HeightRequest = 36, HorizontalOptions = LayoutOptions.Center };
        _methods = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto) }, RowSpacing = 10, ColumnSpacing = 10 };
        var receipt = new CheckBox { IsChecked = true }; receipt.CheckedChanged += (_, e) => { if (_viewModel is not null) _viewModel.PrintReceipt = e.Value; };
        var split = new CheckBox(); split.CheckedChanged += (_, e) => { if (_viewModel is not null) _viewModel.IsSplit = e.Value; };
        _submit = new SharedButton { Text = "Confirm Payment", HeightRequest = 58 }; _submit.Clicked += (_, _) => _viewModel?.SubmitCommand.Execute(null);
        _tendered.TextChanged += (_, _) => { if (_viewModel is not null && decimal.TryParse(_tendered.Text, out var value)) _viewModel.Tendered = value; };
        var left = Card(new VerticalStackLayout { Spacing = 16, Children = { Label("Amount Due", 14, false), _due, _methods, Label("Cash received", 14, false), _tendered, Line("Change", _change), Line("Remaining", _remaining), new HorizontalStackLayout { Spacing = 8, Children = { receipt, Label("Print receipt", 14, false) } }, new HorizontalStackLayout { Spacing = 8, Children = { split, Label("Split / partial payment", 14, false) } }, _waiting, _status, _submit } });
        var right = Card(new VerticalStackLayout { Spacing = 14, Children = { Label("Authoritative payment status", 20, true), Label("Mother POS confirms a payment only after its payment integration returns a final result.", 14, false), Label("If a request times out, check status before retrying. Do not charge again.", 14, false) } });
        var content = new Grid { Padding = 24, ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(360) }, ColumnSpacing = 18 }; content.Use(Grid.BackgroundColorProperty, "OwBackground"); content.Add(left); content.Add(right, 1); Content = content;
    }
    public PaymentViewModel? ViewModel { get => _viewModel; set { if (_viewModel == value) return; if (_viewModel is not null) _viewModel.PropertyChanged -= OnChanged; _viewModel = value; if (value is not null) value.PropertyChanged += OnChanged; Rebuild(); } }
    public event EventHandler<PaymentSubmission>? SubmissionRequested { add { if (_viewModel is not null) _viewModel.SubmissionRequested += value; } remove { if (_viewModel is not null) _viewModel.SubmissionRequested -= value; } }
    private void Rebuild()
    {
        if (_viewModel is null) return;
        _due.Text = Format(_viewModel.AmountDue); _tendered.Text = _viewModel.Tendered.ToString("0.00"); _change.Text = Format(_viewModel.ChangeDue); _remaining.Text = Format(_viewModel.Remaining); _status.Text = _viewModel.Message;
        _waiting.IsVisible = _viewModel.State is PaymentPresentationState.Submitting or PaymentPresentationState.WaitingForCard; _waiting.IsRunning = _waiting.IsVisible;
        _submit.IsEnabled = _viewModel.State is not (PaymentPresentationState.Submitting or PaymentPresentationState.WaitingForCard or PaymentPresentationState.Approved);
        _submit.Text = _viewModel.State == PaymentPresentationState.WaitingForCard ? "Waiting for card…" : _viewModel.State == PaymentPresentationState.Approved ? "Payment Confirmed" : "Confirm Payment";
        _methods.Children.Clear();
        AddMethod("Cash", "cash", 0, 0); AddMethod("Card", "card", 0, 1); AddMethod("Gift Card", "gift_card", 1, 0); AddMethod("Split", "split", 1, 1);
    }
    private void AddMethod(string title, string id, int row, int column)
    {
        var selected = id == "split" ? _viewModel?.IsSplit == true : _viewModel?.SelectedMethod == id;
        var button = new SharedButton { Text = title, Variant = selected ? ButtonVariant.Primary : ButtonVariant.Secondary };
        button.Clicked += (_, _) =>
        {
            if (_viewModel is null) return;
            if (id == "split") _viewModel.IsSplit = true;
            else _viewModel.SelectedMethod = id;
        };
        _methods.Add(button, column, row);
    }
    private static Border Card(View content) { var card = new Border { Padding = 20, StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 }, Content = content }; card.Use(Border.BackgroundColorProperty, "OwSurface"); card.Use(Border.StrokeProperty, "OwBorder"); return card; }
    private static Label Label(string text, double size, bool bold) { var l = new Microsoft.Maui.Controls.Label { Text = text, FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None }; l.Use(Microsoft.Maui.Controls.Label.TextColorProperty, bold ? "OwTextStrong" : "OwTextMuted"); return l; }
    private static Label Money(double size, bool bold) { var l = new Microsoft.Maui.Controls.Label { FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None }; l.Use(Microsoft.Maui.Controls.Label.TextColorProperty, "OwTextStrong"); return l; }
    private static Grid Line(string text, Label value) { var grid = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) } }; grid.Add(Label(text, 14, false)); grid.Add(value, 1); return grid; }
    private static string Format(decimal value) => value.ToString("C2", System.Globalization.CultureInfo.GetCultureInfo("en-GB"));
    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Rebuild();
}

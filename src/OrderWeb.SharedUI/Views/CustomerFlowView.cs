using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

/// <summary>Host-neutral customer search, assignment, and collection/delivery details surface.</summary>
public sealed record CustomerPresentation(string Id, string Name, string Phone, string? Email, string? Address, string? Postcode, string? Detail = null);
public sealed record CustomerHistoryPresentation(string OrderNumber, string Detail, string Status, string? Date = null);
public sealed record CustomerFlowSubmission(string OrderType, CustomerPresentation Customer, string? PickupOrDeliveryTime, string? Address, string? Postcode, string? Notes);

public sealed class CustomerFlowView : ContentView
{
    private readonly Entry _search;
    private readonly VerticalStackLayout _results;
    private readonly Label _state;
    private readonly SharedButton _collection;
    private readonly SharedButton _delivery;
    private readonly Entry _name;
    private readonly Entry _phone;
    private readonly Entry _email;
    private readonly Editor _address;
    private readonly Entry _postcode;
    private readonly Entry _time;
    private readonly Editor _notes;
    private readonly VerticalStackLayout _history;
    private string _orderType = "Collection";
    private CustomerPresentation? _selected;

    public CustomerFlowView()
    {
        _search = new Entry { Placeholder = "Search by name, phone, address, or postcode", FontSize = 16 };
        var search = new SharedButton { Text = "Search", WidthRequest = 112 };
        search.Clicked += (_, _) => SearchRequested?.Invoke(this, _search.Text?.Trim() ?? string.Empty);
        var searchRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 10 };
        searchRow.Add(_search); searchRow.Add(search, 1);

        _state = new Label { FontSize = 13, IsVisible = false }; _state.Use(Label.TextColorProperty, "OwTextMuted");
        _results = new VerticalStackLayout { Spacing = 8 };
        var resultsCard = Card("Customer Search", new VerticalStackLayout { Spacing = 10, Children = { searchRow, _state, _results } });

        _collection = new SharedButton { Text = "Collection", Variant = ButtonVariant.Primary };
        _delivery = new SharedButton { Text = "Delivery", Variant = ButtonVariant.Secondary };
        _collection.Clicked += (_, _) => SetOrderType("Collection");
        _delivery.Clicked += (_, _) => SetOrderType("Delivery");
        var typeRow = new Grid { ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 10 }; typeRow.Add(_collection); typeRow.Add(_delivery, 1);

        _name = Field("Customer name", "Required");
        _phone = Field("Phone", "Required");
        _email = Field("Email", "Optional");
        _address = new Editor { Placeholder = "Delivery address", AutoSize = EditorAutoSizeOption.TextChanges, HeightRequest = 80 };
        _postcode = Field("Postcode", "Delivery postcode");
        _time = Field("Collection/delivery time", "ASAP");
        _notes = new Editor { Placeholder = "Collection or delivery notes", AutoSize = EditorAutoSizeOption.TextChanges, HeightRequest = 70 };
        var continueButton = new SharedButton { Text = "Save & Start Order" };
        continueButton.Clicked += (_, _) => Submit();
        var details = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                typeRow, TextLabel("Customer details"), _name, _phone, _email,
                TextLabel("Delivery details"), _address, _postcode, _time, _notes, continueButton
            }
        };

        _history = new VerticalStackLayout { Spacing = 8 };
        var historyCard = Card("Permitted Order History", _history);
        Content = new ScrollView
        {
            Content = new VerticalStackLayout { Padding = 24, Spacing = 18, MaximumWidthRequest = 900, HorizontalOptions = LayoutOptions.Center, Children = { resultsCard, Card("Order Type & Assignment", details), historyCard } }
        };
        SetResults([]);
        SetHistory([]);
    }

    public event EventHandler<string>? SearchRequested;
    public event EventHandler<CustomerPresentation>? CustomerSelected;
    public event EventHandler<CustomerFlowSubmission>? SubmissionRequested;

    public string OrderType => _orderType;
    public void SetLoading(bool loading, string message = "Searching customers…") { _state.Text = message; _state.IsVisible = true; }
    public void SetStatus(string message) { _state.Text = message; _state.IsVisible = !string.IsNullOrWhiteSpace(message); }
    public void SetResults(IReadOnlyList<CustomerPresentation> customers)
    {
        _results.Children.Clear();
        if (customers.Count == 0)
        {
            _results.Children.Add(new Label { Text = "No customer results. Enter details below to continue.", FontSize = 13 });
            return;
        }
        foreach (var customer in customers)
        {
            var row = new Border { Padding = 12, StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 }, Content = new VerticalStackLayout { Spacing = 2, Children = { TextLabel(customer.Name, 16, true), TextLabel($"{customer.Phone}{(string.IsNullOrWhiteSpace(customer.Address) ? string.Empty : $" · {customer.Address}")}", 13), TextLabel(customer.Detail ?? string.Empty, 12) } } };
            row.Use(Border.BackgroundColorProperty, "OwSurfaceMuted"); row.Use(Border.StrokeProperty, "OwBorder");
            var captured = customer; var tap = new TapGestureRecognizer(); tap.Tapped += (_, _) => Select(captured); row.GestureRecognizers.Add(tap); _results.Children.Add(row);
        }
    }
    public void SetHistory(IReadOnlyList<CustomerHistoryPresentation> history)
    {
        _history.Children.Clear();
        if (history.Count == 0) { _history.Children.Add(TextLabel("No permitted history is available on this terminal.", 13)); return; }
        foreach (var item in history) _history.Children.Add(new StatusBadge { Text = $"{item.OrderNumber} · {item.Detail} · {item.Status}", Kind = item.Status.Contains("closed", StringComparison.OrdinalIgnoreCase) ? StatusKind.Success : StatusKind.Info });
    }

    private void Select(CustomerPresentation customer)
    {
        _selected = customer; _name.Text = customer.Name; _phone.Text = customer.Phone; _email.Text = customer.Email; _address.Text = customer.Address; _postcode.Text = customer.Postcode; SetStatus("Customer selected. Review details before continuing."); CustomerSelected?.Invoke(this, customer);
    }
    private void SetOrderType(string value) { _orderType = value; _collection.Variant = value == "Collection" ? ButtonVariant.Primary : ButtonVariant.Secondary; _delivery.Variant = value == "Delivery" ? ButtonVariant.Primary : ButtonVariant.Secondary; }
    private void Submit()
    {
        var customer = new CustomerPresentation(_selected?.Id ?? string.Empty, _name.Text?.Trim() ?? string.Empty, _phone.Text?.Trim() ?? string.Empty, _email.Text?.Trim(), _address.Text?.Trim(), _postcode.Text?.Trim());
        SubmissionRequested?.Invoke(this, new CustomerFlowSubmission(_orderType, customer, _time.Text?.Trim(), customer.Address, customer.Postcode, _notes.Text?.Trim()));
    }
    private static Entry Field(string placeholder, string hint) => new() { Placeholder = placeholder, AutomationId = hint, FontSize = 16 };
    private static Label TextLabel(string text, double size = 14, bool bold = false) { var value = new Label { Text = text, FontSize = size, FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None }; value.Use(Label.TextColorProperty, bold ? "OwTextStrong" : "OwTextMuted"); return value; }
    private static Border Card(string title, View body) { var heading = TextLabel(title, 18, true); var card = new Border { Padding = 18, StrokeThickness = 1, StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 }, Content = new VerticalStackLayout { Spacing = 12, Children = { heading, body } } }; card.Use(Border.BackgroundColorProperty, "OwSurface"); card.Use(Border.StrokeProperty, "OwBorder"); return card; }
}

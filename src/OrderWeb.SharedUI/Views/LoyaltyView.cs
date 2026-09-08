using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

public sealed record LoyaltyCustomerPresentation(
    string Name,
    string Phone,
    string Email,
    int PointsBalance,
    string LastVisit,
    string Status = "Active");

public sealed record LoyaltyHistoryPresentation(
    string Description,
    string Date,
    string PointsDisplay,
    bool IsCredit);

public sealed record LoyaltyNewCustomerRequest(string Phone, string Name, string? Email);

/// <summary>
/// Host-neutral loyalty search and points surface for Mother and Client POS.
/// Presentation only — hosts own API calls and authorization.
/// </summary>
public sealed class LoyaltyView : ContentView
{
    private readonly Entry _phoneSearch;
    private readonly Entry _points;
    private readonly Entry _notes;
    private readonly Entry _newPhone;
    private readonly Entry _newName;
    private readonly Entry _newEmail;
    private readonly Border _customerPanel;
    private readonly Border _diagnosticsButton;
    private readonly Label _customerName;
    private readonly Label _customerPhone;
    private readonly Label _customerEmail;
    private readonly Label _customerPoints;
    private readonly Label _redemptionValue;
    private readonly Label _lastVisit;
    private readonly StatusBadge _statusBadge;
    private readonly VerticalStackLayout _historyList;
    private readonly Grid _historyOverlay;
    private readonly Grid _newCustomerOverlay;
    private readonly SharedButton _searchButton;
    private readonly SharedButton _addPointsButton;
    private readonly SharedButton _redeemPointsButton;

    public LoyaltyView()
    {
        _phoneSearch = Field("Phone number", Keyboard.Telephone);
        _phoneSearch.Completed += (_, _) => SearchRequested?.Invoke(this, EventArgs.Empty);

        _searchButton = new SharedButton { Text = "Search", HeightRequest = 52, FontSize = 16 };
        _searchButton.Clicked += (_, _) => SearchRequested?.Invoke(this, EventArgs.Empty);

        var newCustomerButton = new SharedButton { Text = "New Customer", Variant = ButtonVariant.Secondary, HeightRequest = 52, FontSize = 16 };
        newCustomerButton.Clicked += (_, _) => NewCustomerRequested?.Invoke(this, EventArgs.Empty);

        var actions = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12
        };
        actions.Add(_searchButton);
        actions.Add(newCustomerButton, 1);

        var testButton = new SharedButton
        {
            Text = "Test API Connection",
            Variant = ButtonVariant.Secondary,
            HeightRequest = 46,
            FontSize = 14
        };
        testButton.Clicked += (_, _) => TestConnectionRequested?.Invoke(this, EventArgs.Empty);
        _diagnosticsButton = new Border
        {
            StrokeThickness = 0,
            IsVisible = true,
            Content = testButton
        };

        var searchCard = SurfaceCard(
            title: "Customer Loyalty",
            subtitle: "Search by phone number, then add or redeem points.",
            body: new VerticalStackLayout
            {
                Spacing = 18,
                Children =
                {
                    LabeledField("Phone number", _phoneSearch),
                    actions,
                    _diagnosticsButton,
                    InfoNote(
                        "Customer data syncs from OrderWeb.net. 100 points equals £1 discount.")
                }
            });

        _customerName = StrongLabel("No name saved", 22);
        _customerPhone = MutedLabel("Phone not saved", 15);
        _customerEmail = MutedLabel("Email not saved", 15);
        _customerPoints = StrongLabel("0", 40);
        _customerPoints.HorizontalTextAlignment = TextAlignment.Center;
        _customerPoints.Use(Label.TextColorProperty, "OwSuccessStrong");
        _redemptionValue = MutedLabel("Worth £0.00", 14);
        _redemptionValue.HorizontalTextAlignment = TextAlignment.Center;
        _lastVisit = MutedLabel("Last visit: Never", 14);
        _statusBadge = new StatusBadge { Text = "Active", Kind = StatusKind.Success, ShowDot = true };

        _points = Field("Points", Keyboard.Numeric);
        _notes = Field("Reason / note");

        _addPointsButton = new SharedButton { Text = "Add Points", Variant = ButtonVariant.Success, HeightRequest = 50, FontSize = 15 };
        _addPointsButton.Clicked += (_, _) => AddPointsRequested?.Invoke(this, EventArgs.Empty);
        _redeemPointsButton = new SharedButton { Text = "Redeem Points", Variant = ButtonVariant.Danger, HeightRequest = 50, FontSize = 15 };
        _redeemPointsButton.Clicked += (_, _) => RedeemPointsRequested?.Invoke(this, EventArgs.Empty);

        var historyButton = OutlineButton("View History");
        historyButton.Clicked += (_, _) => ViewHistoryRequested?.Invoke(this, EventArgs.Empty);
        var statementButton = OutlineButton("Send Statement");
        statementButton.Clicked += (_, _) => SendStatementRequested?.Invoke(this, EventArgs.Empty);

        var detailsHeader = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 12
        };
        detailsHeader.Add(new VerticalStackLayout
        {
            Spacing = 4,
            Children = { StrongLabel("Customer Details", 24), _lastVisit }
        });
        detailsHeader.Add(_statusBadge, 1);

        var infoBlock = InnerPanel(new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                SectionLabel("Customer"),
                _customerName,
                _customerPhone,
                _customerEmail
            }
        });

        var pointsBlock = new Border
        {
            Padding = new Thickness(22, 20),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    SectionLabel("Current points", center: true),
                    _customerPoints,
                    _redemptionValue
                }
            }
        };
        pointsBlock.Use(Border.BackgroundColorProperty, "OwSuccessSoft");
        pointsBlock.Use(Border.StrokeProperty, "OwSuccess");

        var customerGrid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(new GridLength(1.05, GridUnitType.Star)), new ColumnDefinition(new GridLength(0.95, GridUnitType.Star)) },
            ColumnSpacing = 16
        };
        customerGrid.Add(infoBlock);
        customerGrid.Add(pointsBlock, 1);

        var adjustBlock = InnerPanel(new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                StrongLabel("Adjust Points", 17),
                BuildAdjustFields(),
                BuildPointsActions()
            }
        });

        var secondaryActions = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12
        };
        secondaryActions.Add(historyButton);
        secondaryActions.Add(statementButton, 1);

        _customerPanel = SurfaceCard(
            title: null,
            subtitle: null,
            body: new VerticalStackLayout
            {
                Spacing = 20,
                Children = { detailsHeader, customerGrid, adjustBlock, secondaryActions }
            });
        _customerPanel.IsVisible = false;

        var content = new Grid
        {
            Padding = new Thickness(36, 28),
            ColumnDefinitions = { new ColumnDefinition(new GridLength(0.9, GridUnitType.Star)), new ColumnDefinition(new GridLength(1.1, GridUnitType.Star)) },
            ColumnSpacing = 24,
            MaximumWidthRequest = 1500,
            HorizontalOptions = LayoutOptions.Center
        };
        content.Add(searchCard);
        content.Add(_customerPanel, 1);

        _historyList = new VerticalStackLayout { Spacing = 8 };
        _historyOverlay = BuildOverlay(
            "Points History",
            new ScrollView { MaximumHeightRequest = 480, Content = _historyList },
            () =>
            {
                ShowHistory(false);
                HistoryClosed?.Invoke(this, EventArgs.Empty);
            });

        _newPhone = Field("e.g. 07306506797", Keyboard.Telephone);
        _newName = Field("e.g. John Smith");
        _newEmail = Field("e.g. customer@email.com", Keyboard.Email);
        var cancelNew = new SharedButton { Text = "Cancel", Variant = ButtonVariant.Secondary, HeightRequest = 50 };
        cancelNew.Clicked += (_, _) =>
        {
            ShowNewCustomer(false);
            NewCustomerCancelled?.Invoke(this, EventArgs.Empty);
        };
        var saveNew = new SharedButton { Text = "Create Customer", Variant = ButtonVariant.Success, HeightRequest = 50 };
        saveNew.Clicked += (_, _) => CreateCustomerRequested?.Invoke(this, new LoyaltyNewCustomerRequest(
            _newPhone.Text?.Trim() ?? string.Empty,
            _newName.Text?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(_newEmail.Text) ? null : _newEmail.Text.Trim()));

        _newCustomerOverlay = BuildOverlay(
            "Add New Customer",
            new VerticalStackLayout
            {
                Spacing = 18,
                Children =
                {
                    LabeledField("Phone number", _newPhone, required: true),
                    LabeledField("Customer name", _newName, required: true),
                    LabeledField("Email address", _newEmail),
                    InfoNote("A loyalty card number is assigned automatically. The customer can earn points immediately."),
                    BuildTwoColumn(cancelNew, saveNew)
                }
            },
            () =>
            {
                ShowNewCustomer(false);
                NewCustomerCancelled?.Invoke(this, EventArgs.Empty);
            },
            maxWidth: 560);

        var root = new Grid();
        root.Use(Grid.BackgroundColorProperty, "OwBackground");
        root.Add(new ScrollView { Content = content });
        root.Add(_historyOverlay);
        root.Add(_newCustomerOverlay);
        Content = root;
    }

    public event EventHandler? SearchRequested;
    public event EventHandler? NewCustomerRequested;
    public event EventHandler? TestConnectionRequested;
    public event EventHandler? AddPointsRequested;
    public event EventHandler? RedeemPointsRequested;
    public event EventHandler? ViewHistoryRequested;
    public event EventHandler? SendStatementRequested;
    public event EventHandler? HistoryClosed;
    public event EventHandler? NewCustomerCancelled;
    public event EventHandler<LoyaltyNewCustomerRequest>? CreateCustomerRequested;

    public string PhoneSearchText
    {
        get => _phoneSearch.Text?.Trim() ?? string.Empty;
        set => _phoneSearch.Text = value;
    }

    public string PointsText
    {
        get => _points.Text?.Trim() ?? string.Empty;
        set => _points.Text = value;
    }

    public string NotesText
    {
        get => _notes.Text?.Trim() ?? string.Empty;
        set => _notes.Text = value;
    }

    public bool ShowDiagnostics
    {
        get => _diagnosticsButton.IsVisible;
        set => _diagnosticsButton.IsVisible = value;
    }

    public bool IsSearchEnabled
    {
        get => _searchButton.IsEnabled;
        set
        {
            _searchButton.IsEnabled = value;
            _phoneSearch.IsEnabled = value;
        }
    }

    public void SetCustomer(LoyaltyCustomerPresentation? customer)
    {
        if (customer is null)
        {
            _customerPanel.IsVisible = false;
            return;
        }

        _customerName.Text = string.IsNullOrWhiteSpace(customer.Name) ? "No name saved" : customer.Name;
        _customerPhone.Text = string.IsNullOrWhiteSpace(customer.Phone) ? "Phone not saved" : customer.Phone;
        _customerEmail.Text = string.IsNullOrWhiteSpace(customer.Email) ? "Email not saved" : customer.Email;
        _customerPoints.Text = $"{customer.PointsBalance:N0}";
        _redemptionValue.Text = $"Worth £{customer.PointsBalance / 100m:F2}";
        _lastVisit.Text = customer.LastVisit;
        _statusBadge.Text = string.IsNullOrWhiteSpace(customer.Status) ? "Active" : customer.Status;
        _customerPanel.IsVisible = true;
    }

    public void ClearAdjustmentFields()
    {
        _points.Text = string.Empty;
        _notes.Text = string.Empty;
    }

    public void SetHistory(IReadOnlyList<LoyaltyHistoryPresentation> history)
    {
        _historyList.Children.Clear();
        if (history.Count == 0)
        {
            _historyList.Children.Add(MutedLabel("No points history yet.", 14));
            return;
        }

        foreach (var item in history)
        {
            var points = StrongLabel(item.PointsDisplay, 18);
            points.VerticalTextAlignment = TextAlignment.Center;
            points.Use(Label.TextColorProperty, item.IsCredit ? "OwSuccessStrong" : "OwErrorStrong");

            var row = new Border
            {
                Padding = new Thickness(16, 14),
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                Content = new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
                    ColumnSpacing = 12,
                }
            };
            var historyRow = (Grid)row.Content!;
            historyRow.Add(new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    StrongLabel(item.Description, 15),
                    MutedLabel(item.Date, 13)
                }
            });
            historyRow.Add(points, 1);
            row.Use(Border.BackgroundColorProperty, "OwSurfaceMuted");
            row.Use(Border.StrokeProperty, "OwBorder");
            _historyList.Children.Add(row);
        }
    }

    public void ShowHistory(bool show) => _historyOverlay.IsVisible = show;

    public void ShowNewCustomer(bool show, string? prefillPhone = null)
    {
        if (show)
        {
            _newPhone.Text = prefillPhone?.Trim() ?? string.Empty;
            _newName.Text = string.Empty;
            _newEmail.Text = string.Empty;
        }

        _newCustomerOverlay.IsVisible = show;
        if (!show)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_newPhone.Text))
        {
            _newPhone.Focus();
        }
        else
        {
            _newName.Focus();
        }
    }

    public void SetPointsBusy(bool busy, bool? adding = null)
    {
        _addPointsButton.IsEnabled = !busy;
        _redeemPointsButton.IsEnabled = !busy;
        if (!busy)
        {
            _addPointsButton.Text = "Add Points";
            _redeemPointsButton.Text = "Redeem Points";
            return;
        }

        if (adding == true)
        {
            _addPointsButton.Text = "Adding…";
        }
        else if (adding == false)
        {
            _redeemPointsButton.Text = "Redeeming…";
        }
    }

    private static Grid BuildOverlay(string title, View body, Action onClose, double maxWidth = 820)
    {
        var close = new Button
        {
            Text = "Close",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            Padding = new Thickness(14, 8),
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0
        };
        close.Use(Button.TextColorProperty, "OwTextMuted");
        close.Clicked += (_, _) => onClose();

        var panel = new Border
        {
            Padding = new Thickness(36, 32),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            MaximumWidthRequest = maxWidth,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 20,
                Children =
                {
                    BuildHeader(title, close),
                    Divider(),
                    body
                }
            }
        };
        panel.Use(Border.BackgroundColorProperty, "OwSurface");
        panel.Use(Border.StrokeProperty, "OwBorder");
        panel.GestureRecognizers.Add(new TapGestureRecognizer());

        var overlay = new Grid
        {
            IsVisible = false,
            ZIndex = 1000
        };
        overlay.Use(Grid.BackgroundColorProperty, "OwOverlay");
        var backdrop = new BoxView { Color = Colors.Transparent };
        backdrop.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(onClose) });
        overlay.Add(backdrop);
        overlay.Add(panel);
        return overlay;
    }

    private static Border SurfaceCard(string? title, string? subtitle, View body)
    {
        var stack = new VerticalStackLayout { Spacing = 18 };
        if (!string.IsNullOrWhiteSpace(title))
        {
            stack.Children.Add(StrongLabel(title!, 26));
        }

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            stack.Children.Add(MutedLabel(subtitle!, 14));
        }

        stack.Children.Add(body);

        var card = new Border
        {
            Padding = 28,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            VerticalOptions = LayoutOptions.Start,
            Content = stack,
            Shadow = new Shadow
            {
                Brush = Color.FromArgb("#0F172A"),
                Offset = new Point(0, 8),
                Radius = 24,
                Opacity = 0.06f
            }
        };
        card.Use(Border.BackgroundColorProperty, "OwSurface");
        card.Use(Border.StrokeProperty, "OwBorder");
        return card;
    }

    private static Border InnerPanel(View content)
    {
        var panel = new Border
        {
            Padding = 18,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = content
        };
        panel.Use(Border.BackgroundColorProperty, "OwSurfaceMuted");
        panel.Use(Border.StrokeProperty, "OwBorder");
        return panel;
    }

    private Grid BuildAdjustFields()
    {
        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(new GridLength(0.9, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(1.1, GridUnitType.Star))
            },
            ColumnSpacing = 12
        };
        grid.Add(LabeledField("Points", _points));
        grid.Add(LabeledField("Reason", _notes), 1);
        return grid;
    }

    private Grid BuildPointsActions()
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12
        };
        grid.Add(_addPointsButton);
        grid.Add(_redeemPointsButton, 1);
        return grid;
    }

    private static Grid BuildTwoColumn(View left, View right)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 12
        };
        grid.Add(left);
        grid.Add(right, 1);
        return grid;
    }

    private static Grid BuildHeader(string title, View close)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        grid.Add(StrongLabel(title, 24));
        grid.Add(close, 1);
        return grid;
    }

    private static Border InfoNote(string text)
    {
        var heading = StrongLabel("Information", 14);
        heading.Use(Label.TextColorProperty, "OwInfoText");
        var body = MutedLabel(text, 13);
        body.Use(Label.TextColorProperty, "OwInfoText");

        var note = new Border
        {
            Padding = new Thickness(16, 14),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = new VerticalStackLayout
            {
                Spacing = 4,
                Children = { heading, body }
            }
        };
        note.Use(Border.BackgroundColorProperty, "OwInfoSoft");
        note.Use(Border.StrokeProperty, "OwPrimarySoftBorder");
        return note;
    }

    private static View LabeledField(string label, Entry entry, bool required = false)
    {
        var caption = SectionLabel(required ? $"{label} *" : label);
        var shell = new Border
        {
            Padding = new Thickness(14, 4),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = entry
        };
        shell.Use(Border.BackgroundColorProperty, "OwBackground");
        shell.Use(Border.StrokeProperty, "OwInputBorder");
        return new VerticalStackLayout { Spacing = 8, Children = { caption, shell } };
    }

    private static Entry Field(string placeholder, Keyboard? keyboard = null)
    {
        var entry = new Entry
        {
            Placeholder = placeholder,
            FontSize = 16,
            BackgroundColor = Colors.Transparent,
            HeightRequest = 46,
            Keyboard = keyboard ?? Keyboard.Default
        };
        entry.Use(Entry.TextColorProperty, "OwTextPrimary");
        entry.Use(Entry.PlaceholderColorProperty, "OwTextPlaceholder");
        return entry;
    }

    private static SharedButton OutlineButton(string text)
    {
        var button = new SharedButton
        {
            Text = text,
            Variant = ButtonVariant.Secondary,
            HeightRequest = 48,
            FontSize = 14
        };
        return button;
    }

    private static BoxView Divider()
    {
        var line = new BoxView { HeightRequest = 1 };
        line.Use(BoxView.ColorProperty, "OwBorder");
        return line;
    }

    private static Label StrongLabel(string text, double size)
    {
        var label = new Label
        {
            Text = text,
            FontSize = size,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.WordWrap
        };
        label.Use(Label.TextColorProperty, "OwTextStrong");
        return label;
    }

    private static Label MutedLabel(string text, double size)
    {
        var label = new Label
        {
            Text = text,
            FontSize = size,
            LineBreakMode = LineBreakMode.WordWrap
        };
        label.Use(Label.TextColorProperty, "OwTextMuted");
        return label;
    }

    private static Label SectionLabel(string text, bool center = false)
    {
        var label = new Label
        {
            Text = text.ToUpperInvariant(),
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            CharacterSpacing = 0.6,
            HorizontalTextAlignment = center ? TextAlignment.Center : TextAlignment.Start
        };
        label.Use(Label.TextColorProperty, "OwTextMuted");
        return label;
    }
}

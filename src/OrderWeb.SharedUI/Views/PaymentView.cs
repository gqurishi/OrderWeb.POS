using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.ViewModels;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Canonical SharedUI tender screen (Mother chrome) for Cash / Card / Gift / Loyalty.
/// COL/DEL: AllowSplit=false forces full bill. Cash: quick tenders + on-screen keypad.
/// Both hosts should call this after PaymentWizard setup (Phase 1).
/// </summary>
public sealed class PaymentView : ContentView
{
    private static readonly CultureInfo Gb = CultureInfo.GetCultureInfo("en-GB");

    private readonly Label _dueValue;
    private readonly Label _tenderedDisplay;
    private readonly Label _changeValue;
    private readonly Label _remainingValue;
    private readonly Border _changeCard;
    private readonly Border _remainingCard;
    private readonly Label _status;
    private readonly Label _policyHint;
    private readonly ChefLoaderView _waiting;
    private readonly Button _submit;
    private readonly Grid _methods;
    private readonly VerticalStackLayout _cashPanel;
    private readonly HorizontalStackLayout _splitRow;
    private readonly Grid _keypad;
    private PaymentViewModel? _viewModel;
    private string _keypadBuffer = string.Empty;

    public PaymentView()
    {
        _dueValue = new Label
        {
            FontSize = 36,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#B45309"),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center
        };

        _tenderedDisplay = new Label
        {
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#0F172A")
        };

        _changeValue = new Label
        {
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#047857"),
            VerticalOptions = LayoutOptions.Center
        };

        _remainingValue = new Label
        {
            FontSize = 20,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#DC2626"),
            VerticalOptions = LayoutOptions.Center
        };

        _status = new Label
        {
            FontSize = 14,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap,
            TextColor = Color.FromArgb("#64748B")
        };

        _policyHint = new Label
        {
            FontSize = 13,
            LineBreakMode = LineBreakMode.WordWrap,
            IsVisible = false,
            TextColor = Color.FromArgb("#64748B")
        };

        _waiting = new ChefLoaderView
        {
            Mode = ChefLoaderMode.Inline,
            Size = ChefLoaderSize.Sm,
            Message = "Processing payment",
            DelayMilliseconds = 0,
            IsLoading = false,
            HorizontalOptions = LayoutOptions.Center
        };

        _methods = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Star) },
            RowDefinitions = { new(GridLength.Auto), new(GridLength.Auto) },
            RowSpacing = 10,
            ColumnSpacing = 10
        };

        _keypad = BuildKeypad();
        _cashPanel = new VerticalStackLayout
        {
            Spacing = 12,
            Children =
            {
                new Label
                {
                    Text = "Cash received",
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#334155")
                },
                _tenderedDisplay,
                BuildQuickTenders(),
                _keypad
            }
        };

        _changeCard = BannerCard(
            "#ECFDF5",
            "#10B981",
            new Label
            {
                Text = "CHANGE:",
                FontSize = 20,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#047857"),
                VerticalOptions = LayoutOptions.Center
            },
            _changeValue);

        _remainingCard = BannerCard(
            "#FEF2F2",
            "#EF4444",
            new Label
            {
                Text = "REMAINING:",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#DC2626"),
                VerticalOptions = LayoutOptions.Center
            },
            _remainingValue);
        _remainingCard.IsVisible = false;

        var receipt = new CheckBox { IsChecked = true };
        receipt.CheckedChanged += (_, e) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.PrintReceipt = e.Value;
            }
        };

        var split = new CheckBox();
        split.CheckedChanged += (_, e) =>
        {
            if (_viewModel is not null)
            {
                _viewModel.IsSplit = e.Value;
            }
        };
        _splitRow = new HorizontalStackLayout
        {
            Spacing = 8,
            IsVisible = false,
            Children =
            {
                split,
                new Label { Text = "Split / partial payment", FontSize = 14, TextColor = Color.FromArgb("#475569") }
            }
        };

        _submit = new Button
        {
            Text = "CONFIRM PAYMENT",
            BackgroundColor = Color.FromArgb("#059669"),
            TextColor = Colors.White,
            FontAttributes = FontAttributes.Bold,
            FontSize = 18,
            CornerRadius = 14,
            HeightRequest = 58
        };
        _submit.Clicked += (_, _) => _viewModel?.SubmitCommand.Execute(null);

        var dueBanner = BannerCard(
            "#FFFBEB",
            "#F59E0B",
            new Label
            {
                Text = "AMOUNT DUE:",
                FontSize = 18,
                FontAttributes = FontAttributes.Bold,
                TextColor = Color.FromArgb("#92400E"),
                VerticalOptions = LayoutOptions.Center
            },
            _dueValue);

        var left = SurfaceCard(new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                dueBanner,
                _policyHint,
                _methods,
                _cashPanel,
                _changeCard,
                _remainingCard,
                new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        receipt,
                        new Label { Text = "Print receipt", FontSize = 14, TextColor = Color.FromArgb("#475569") }
                    }
                },
                _splitRow,
                _waiting,
                _status,
                _submit
            }
        });

        var right = SurfaceCard(new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                new Label
                {
                    Text = "Authoritative payment status",
                    FontSize = 20,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#0F172A")
                },
                new Label
                {
                    Text = "Mother POS confirms a payment only after its payment integration returns a final result.",
                    FontSize = 14,
                    TextColor = Color.FromArgb("#64748B"),
                    LineBreakMode = LineBreakMode.WordWrap
                },
                new Label
                {
                    Text = "If a request times out, check status before retrying. Do not charge again.",
                    FontSize = 14,
                    TextColor = Color.FromArgb("#64748B"),
                    LineBreakMode = LineBreakMode.WordWrap
                }
            }
        });

        var content = new Grid
        {
            Padding = 24,
            ColumnDefinitions = { new(GridLength.Star), new(new GridLength(340)) },
            ColumnSpacing = 18,
            BackgroundColor = Color.FromArgb("#F8FAFC")
        };
        content.Add(left);
        content.Add(right, 1);
        Content = new ScrollView { Content = content };
    }

    public PaymentViewModel? ViewModel
    {
        get => _viewModel;
        set
        {
            if (_viewModel == value)
            {
                return;
            }

            if (_viewModel is not null)
            {
                _viewModel.PropertyChanged -= OnChanged;
            }

            _viewModel = value;
            if (value is not null)
            {
                value.PropertyChanged += OnChanged;
                _keypadBuffer = value.Tendered > 0
                    ? value.Tendered.ToString("0.##", CultureInfo.InvariantCulture)
                    : string.Empty;
            }

            Rebuild();
        }
    }

    public event EventHandler<PaymentSubmission>? SubmissionRequested
    {
        add
        {
            if (_viewModel is not null)
            {
                _viewModel.SubmissionRequested += value;
            }
        }
        remove
        {
            if (_viewModel is not null)
            {
                _viewModel.SubmissionRequested -= value;
            }
        }
    }

    private void Rebuild()
    {
        if (_viewModel is null)
        {
            return;
        }

        _dueValue.Text = Format(_viewModel.AmountDue);
        _tenderedDisplay.Text = Format(_viewModel.Tendered);
        _changeValue.Text = Format(_viewModel.ChangeDue);
        _remainingValue.Text = Format(_viewModel.Remaining);
        _status.Text = _viewModel.Message;

        var allowSplit = _viewModel.AllowSplit;
        _policyHint.IsVisible = !allowSplit;
        _policyHint.Text = allowSplit
            ? string.Empty
            : "Collection / Delivery: full bill only (same as Mother). Split and partial tender are disabled.";
        _splitRow.IsVisible = allowSplit && _viewModel.ShowInlineSplitToggle;

        var isCash = string.Equals(_viewModel.SelectedMethod, "cash", StringComparison.OrdinalIgnoreCase);
        _cashPanel.IsVisible = isCash;
        _changeCard.IsVisible = isCash;
        _remainingCard.IsVisible = isCash && _viewModel.Remaining > 0.009m && _viewModel.EffectiveSplit;

        var waiting = _viewModel.State is PaymentPresentationState.Submitting or PaymentPresentationState.WaitingForCard;
        _waiting.IsLoading = waiting;
        _waiting.Message = _viewModel.State == PaymentPresentationState.WaitingForCard
            ? "Waiting for card"
            : "Processing payment";
        _submit.IsEnabled = _viewModel.State is not (PaymentPresentationState.Submitting
            or PaymentPresentationState.WaitingForCard
            or PaymentPresentationState.Approved);
        _submit.Text = _viewModel.State switch
        {
            PaymentPresentationState.WaitingForCard => "Waiting for card…",
            PaymentPresentationState.Approved => "Payment Confirmed",
            _ => "CONFIRM PAYMENT"
        };

        _methods.Children.Clear();
        _methods.RowDefinitions.Clear();
        _methods.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        _methods.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        AddMethod("Cash", "cash", 0, 0);
        AddMethod("Card", "card", 0, 1);
        AddMethod("Gift Card", "gift_card", 1, 0);
        if (_viewModel.ShowLoyaltyMethod)
        {
            AddMethod("Loyalty", "loyalty", 1, 1);
        }

        if (allowSplit && _viewModel.ShowInlineSplitMethod)
        {
            _methods.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            AddMethod("Split", "split", 2, 0);
        }
    }

    private void AddMethod(string title, string id, int row, int column)
    {
        var selected = id == "split"
            ? _viewModel?.EffectiveSplit == true
            : string.Equals(_viewModel?.SelectedMethod, id, StringComparison.OrdinalIgnoreCase);

        var button = new Button
        {
            Text = title,
            HeightRequest = 56,
            CornerRadius = 14,
            FontAttributes = FontAttributes.Bold,
            FontSize = 16,
            BackgroundColor = selected == true ? Color.FromArgb("#2563EB") : Color.FromArgb("#F1F5F9"),
            TextColor = selected == true ? Colors.White : Color.FromArgb("#0F172A"),
            BorderColor = selected == true ? Color.FromArgb("#2563EB") : Color.FromArgb("#CBD5E1"),
            BorderWidth = 1
        };
        button.Clicked += (_, _) =>
        {
            if (_viewModel is null)
            {
                return;
            }

            if (id == "split")
            {
                if (!_viewModel.AllowSplit)
                {
                    return;
                }

                _viewModel.IsSplit = true;
                return;
            }

            _viewModel.SelectedMethod = id;
            if (id == "cash" && _viewModel.Tendered <= 0)
            {
                _viewModel.SetTenderedExact();
                _keypadBuffer = _viewModel.Tendered.ToString("0.##", CultureInfo.InvariantCulture);
            }
        };
        _methods.Add(button, column, row);
    }

    private View BuildQuickTenders()
    {
        var row = new Grid
        {
            ColumnDefinitions =
            {
                new(GridLength.Star),
                new(GridLength.Star),
                new(GridLength.Star),
                new(GridLength.Star),
                new(GridLength.Star)
            },
            ColumnSpacing = 8
        };

        void AddChip(int column, string text, Action action, Color bg, Color fg)
        {
            var button = new Button
            {
                Text = text,
                HeightRequest = 44,
                CornerRadius = 12,
                FontAttributes = FontAttributes.Bold,
                FontSize = 13,
                BackgroundColor = bg,
                TextColor = fg,
                Padding = 0
            };
            button.Clicked += (_, _) =>
            {
                action();
                SyncKeypadFromTendered();
            };
            row.Add(button, column);
        }

        AddChip(0, "Exact", () => _viewModel?.SetTenderedExact(), Color.FromArgb("#D1FAE5"), Color.FromArgb("#065F46"));
        AddChip(1, "Round £", () =>
        {
            if (_viewModel is null) return;
            _viewModel.SetTenderedQuick(Math.Ceiling(_viewModel.AmountDue));
        }, Color.FromArgb("#E2E8F0"), Color.FromArgb("#1E293B"));
        AddChip(2, "+£5", () =>
        {
            if (_viewModel is null) return;
            _viewModel.SetTenderedQuick(Math.Ceiling(_viewModel.AmountDue / 5m) * 5m);
        }, Color.FromArgb("#DBEAFE"), Color.FromArgb("#1E40AF"));
        AddChip(3, "+£10", () =>
        {
            if (_viewModel is null) return;
            _viewModel.SetTenderedQuick(Math.Ceiling(_viewModel.AmountDue / 10m) * 10m);
        }, Color.FromArgb("#DBEAFE"), Color.FromArgb("#1E40AF"));
        AddChip(4, "+£20", () =>
        {
            if (_viewModel is null) return;
            _viewModel.SetTenderedQuick(Math.Ceiling(_viewModel.AmountDue / 20m) * 20m);
        }, Color.FromArgb("#DBEAFE"), Color.FromArgb("#1E40AF"));

        return row;
    }

    private Grid BuildKeypad()
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new(GridLength.Auto),
                new(GridLength.Auto),
                new(GridLength.Auto),
                new(GridLength.Auto)
            },
            ColumnDefinitions =
            {
                new(GridLength.Star),
                new(GridLength.Star),
                new(GridLength.Star)
            },
            RowSpacing = 8,
            ColumnSpacing = 8
        };

        void AddKey(string label, int column, int row, Action action, bool danger = false)
        {
            var button = new Button
            {
                Text = label,
                HeightRequest = 58,
                CornerRadius = 14,
                FontAttributes = FontAttributes.Bold,
                FontSize = 20,
                BackgroundColor = danger ? Color.FromArgb("#FEE2E2") : Color.FromArgb("#E2E8F0"),
                TextColor = danger ? Color.FromArgb("#B91C1C") : Color.FromArgb("#0F172A"),
                Padding = 0
            };
            button.Clicked += (_, _) => action();
            grid.Add(button, column, row);
        }

        AddKey("1", 0, 0, () => AppendKey("1"));
        AddKey("2", 1, 0, () => AppendKey("2"));
        AddKey("3", 2, 0, () => AppendKey("3"));
        AddKey("4", 0, 1, () => AppendKey("4"));
        AddKey("5", 1, 1, () => AppendKey("5"));
        AddKey("6", 2, 1, () => AppendKey("6"));
        AddKey("7", 0, 2, () => AppendKey("7"));
        AddKey("8", 1, 2, () => AppendKey("8"));
        AddKey("9", 2, 2, () => AppendKey("9"));
        AddKey(".", 0, 3, () => AppendKey("."));
        AddKey("0", 1, 3, () => AppendKey("0"));
        AddKey("⌫", 2, 3, BackspaceKey, danger: true);

        return grid;
    }

    private void AppendKey(string key)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (key == ".")
        {
            if (_keypadBuffer.Contains('.'))
            {
                return;
            }

            _keypadBuffer = string.IsNullOrEmpty(_keypadBuffer) ? "0." : _keypadBuffer + ".";
        }
        else
        {
            if (_keypadBuffer.Contains('.'))
            {
                var decimals = _keypadBuffer.Split('.')[1];
                if (decimals.Length >= 2)
                {
                    return;
                }
            }

            if (_keypadBuffer is "0")
            {
                _keypadBuffer = key;
            }
            else if (_keypadBuffer.Length < 10)
            {
                _keypadBuffer += key;
            }
        }

        ApplyKeypadBuffer();
    }

    private void BackspaceKey()
    {
        if (_keypadBuffer.Length == 0)
        {
            return;
        }

        _keypadBuffer = _keypadBuffer[..^1];
        ApplyKeypadBuffer();
    }

    private void ApplyKeypadBuffer()
    {
        if (_viewModel is null)
        {
            return;
        }

        if (string.IsNullOrEmpty(_keypadBuffer) || _keypadBuffer == ".")
        {
            _viewModel.Tendered = 0m;
            return;
        }

        if (decimal.TryParse(_keypadBuffer, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
        {
            _viewModel.Tendered = value;
        }
    }

    private void SyncKeypadFromTendered()
    {
        if (_viewModel is null)
        {
            return;
        }

        _keypadBuffer = _viewModel.Tendered > 0
            ? _viewModel.Tendered.ToString("0.##", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private static Border BannerCard(string bg, string stroke, View left, View right)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new(GridLength.Star), new(GridLength.Auto) },
            Children = { left }
        };
        grid.Add(right, 1);
        return new Border
        {
            BackgroundColor = Color.FromArgb(bg),
            Stroke = Color.FromArgb(stroke),
            StrokeThickness = 2,
            Padding = 16,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = grid
        };
    }

    private static Border SurfaceCard(View content) => new()
    {
        Padding = 20,
        StrokeThickness = 1,
        Stroke = Color.FromArgb("#E2E8F0"),
        BackgroundColor = Colors.White,
        StrokeShape = new RoundRectangle { CornerRadius = 18 },
        Content = content
    };

    private static string Format(decimal value) => value.ToString("C2", Gb);

    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PaymentViewModel.Tendered) or nameof(PaymentViewModel.AmountDue))
        {
            if (_viewModel is not null &&
                decimal.TryParse(_keypadBuffer, NumberStyles.Number, CultureInfo.InvariantCulture, out var buffered) &&
                buffered != _viewModel.Tendered)
            {
                SyncKeypadFromTendered();
            }
            else if (_viewModel is not null && string.IsNullOrEmpty(_keypadBuffer) && _viewModel.Tendered > 0)
            {
                SyncKeypadFromTendered();
            }
        }

        Rebuild();
    }
}

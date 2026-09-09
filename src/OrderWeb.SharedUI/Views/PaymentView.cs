using System.Globalization;
using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.ViewModels;

namespace OrderWeb.SharedUI.Views;

/// <summary>
/// Canonical Mother-style payment UI. It cannot declare payment success itself.
/// COL/DEL: hide Split and force full bill. Cash: quick tenders + on-screen currency keypad.
/// </summary>
public sealed class PaymentView : ContentView
{
    private static readonly CultureInfo Gb = CultureInfo.GetCultureInfo("en-GB");

    private readonly Microsoft.Maui.Controls.Label _due;
    private readonly Microsoft.Maui.Controls.Label _tenderedDisplay;
    private readonly Microsoft.Maui.Controls.Label _change;
    private readonly Microsoft.Maui.Controls.Label _remaining;
    private readonly Microsoft.Maui.Controls.Label _status;
    private readonly Microsoft.Maui.Controls.Label _policyHint;
    private readonly ChefLoaderView _waiting;
    private readonly SharedButton _submit;
    private readonly Grid _methods;
    private readonly VerticalStackLayout _cashPanel;
    private readonly HorizontalStackLayout _splitRow;
    private readonly Grid _keypad;
    private PaymentViewModel? _viewModel;
    private string _keypadBuffer = string.Empty;

    public PaymentView()
    {
        _due = Money(36, true);
        _tenderedDisplay = Money(28, true);
        _change = Money(18, true);
        _remaining = Money(18, true);
        _status = new Microsoft.Maui.Controls.Label
        {
            FontSize = 14,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };
        _status.Use(Microsoft.Maui.Controls.Label.TextColorProperty, "OwTextMuted");
        _policyHint = new Microsoft.Maui.Controls.Label
        {
            FontSize = 13,
            LineBreakMode = LineBreakMode.WordWrap,
            IsVisible = false
        };
        _policyHint.Use(Microsoft.Maui.Controls.Label.TextColorProperty, "OwTextMuted");

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
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 10,
            ColumnSpacing = 10
        };

        _keypad = BuildKeypad();
        _cashPanel = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                Label("Cash received", 14, false),
                _tenderedDisplay,
                BuildQuickTenders(),
                _keypad
            }
        };

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
            Children = { split, Label("Split / partial payment", 14, false) }
        };

        _submit = new SharedButton { Text = "Confirm Payment", HeightRequest = 58 };
        _submit.Clicked += (_, _) => _viewModel?.SubmitCommand.Execute(null);

        var left = Card(new VerticalStackLayout
        {
            Spacing = 16,
            Children =
            {
                Label("Amount Due", 14, false),
                _due,
                _policyHint,
                _methods,
                _cashPanel,
                Line("Change", _change),
                Line("Remaining", _remaining),
                new HorizontalStackLayout { Spacing = 8, Children = { receipt, Label("Print receipt", 14, false) } },
                _splitRow,
                _waiting,
                _status,
                _submit
            }
        });

        var right = Card(new VerticalStackLayout
        {
            Spacing = 14,
            Children =
            {
                Label("Authoritative payment status", 20, true),
                Label("Mother POS confirms a payment only after its payment integration returns a final result.", 14, false),
                Label("If a request times out, check status before retrying. Do not charge again.", 14, false)
            }
        });

        var content = new Grid
        {
            Padding = 24,
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(360) },
            ColumnSpacing = 18
        };
        content.Use(Grid.BackgroundColorProperty, "OwBackground");
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
                _keypadBuffer = value.Tendered > 0 ? value.Tendered.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
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

        _due.Text = Format(_viewModel.AmountDue);
        _tenderedDisplay.Text = Format(_viewModel.Tendered);
        _change.Text = Format(_viewModel.ChangeDue);
        _remaining.Text = Format(_viewModel.Remaining);
        _status.Text = _viewModel.Message;

        var allowSplit = _viewModel.AllowSplit;
        _policyHint.IsVisible = !allowSplit;
        _policyHint.Text = allowSplit
            ? string.Empty
            : "Collection / Delivery: full bill only (same as Mother). Split and partial tender are disabled.";
        _splitRow.IsVisible = allowSplit;

        var isCash = string.Equals(_viewModel.SelectedMethod, "cash", StringComparison.OrdinalIgnoreCase);
        _cashPanel.IsVisible = isCash;

        var waiting = _viewModel.State is PaymentPresentationState.Submitting or PaymentPresentationState.WaitingForCard;
        _waiting.IsLoading = waiting;
        _waiting.Message = _viewModel.State == PaymentPresentationState.WaitingForCard
            ? "Waiting for card"
            : "Processing payment";
        _submit.IsEnabled = _viewModel.State is not (PaymentPresentationState.Submitting
            or PaymentPresentationState.WaitingForCard
            or PaymentPresentationState.Approved);
        _submit.Text = _viewModel.State == PaymentPresentationState.WaitingForCard
            ? "Waiting for card…"
            : _viewModel.State == PaymentPresentationState.Approved
                ? "Payment Confirmed"
                : "Confirm Payment";

        _methods.Children.Clear();
        AddMethod("Cash", "cash", 0, 0);
        AddMethod("Card", "card", 0, 1);
        AddMethod("Gift Card", "gift_card", 1, 0);
        AddMethod("Loyalty", "loyalty", 1, 1);
        if (allowSplit)
        {
            AddMethod("Split", "split", 2, 0);
        }
    }

    private void AddMethod(string title, string id, int row, int column)
    {
        var selected = id == "split"
            ? _viewModel?.EffectiveSplit == true
            : _viewModel?.SelectedMethod == id;
        var button = new SharedButton
        {
            Text = title,
            Variant = selected == true ? ButtonVariant.Primary : ButtonVariant.Secondary,
            HeightRequest = 52
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
            }
            else
            {
                _viewModel.SelectedMethod = id;
                if (id == "cash" && _viewModel.Tendered <= 0)
                {
                    _viewModel.SetTenderedExact();
                    _keypadBuffer = _viewModel.Tendered.ToString("0.##", CultureInfo.InvariantCulture);
                }
            }
        };
        _methods.Add(button, column, row);
    }

    private View BuildQuickTenders()
    {
        var row = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Start
        };

        void AddChip(string text, Action action)
        {
            var button = new SharedButton
            {
                Text = text,
                Variant = ButtonVariant.Secondary,
                HeightRequest = 44,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(14, 0)
            };
            button.Clicked += (_, _) =>
            {
                action();
                SyncKeypadFromTendered();
            };
            row.Children.Add(button);
        }

        AddChip("Exact", () => _viewModel?.SetTenderedExact());
        AddChip("Round £", () =>
        {
            if (_viewModel is null)
            {
                return;
            }

            var rounded = Math.Ceiling(_viewModel.AmountDue);
            _viewModel.SetTenderedQuick(rounded);
        });
        AddChip("+£5", () =>
        {
            if (_viewModel is null)
            {
                return;
            }

            _viewModel.SetTenderedQuick(Math.Ceiling(_viewModel.AmountDue / 5m) * 5m);
        });
        AddChip("+£10", () =>
        {
            if (_viewModel is null)
            {
                return;
            }

            _viewModel.SetTenderedQuick(Math.Ceiling(_viewModel.AmountDue / 10m) * 10m);
        });
        AddChip("+£20", () =>
        {
            if (_viewModel is null)
            {
                return;
            }

            _viewModel.SetTenderedQuick(Math.Ceiling(_viewModel.AmountDue / 20m) * 20m);
        });

        return row;
    }

    private Grid BuildKeypad()
    {
        var grid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            RowSpacing = 8,
            ColumnSpacing = 8
        };

        void AddKey(string label, int column, int row, Action action)
        {
            var button = new SharedButton
            {
                Text = label,
                Variant = ButtonVariant.Secondary,
                HeightRequest = 56
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
        AddKey("⌫", 2, 3, BackspaceKey);

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

    private static Border Card(View content)
    {
        var card = new Border
        {
            Padding = 20,
            StrokeThickness = 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 },
            Content = content
        };
        card.Use(Border.BackgroundColorProperty, "OwSurface");
        card.Use(Border.StrokeProperty, "OwBorder");
        return card;
    }

    private static Microsoft.Maui.Controls.Label Label(string text, double size, bool bold)
    {
        var label = new Microsoft.Maui.Controls.Label
        {
            Text = text,
            FontSize = size,
            FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None
        };
        label.Use(Microsoft.Maui.Controls.Label.TextColorProperty, bold ? "OwTextStrong" : "OwTextMuted");
        return label;
    }

    private static Microsoft.Maui.Controls.Label Money(double size, bool bold)
    {
        var label = new Microsoft.Maui.Controls.Label
        {
            FontSize = size,
            FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None
        };
        label.Use(Microsoft.Maui.Controls.Label.TextColorProperty, "OwTextStrong");
        return label;
    }

    private static Grid Line(string text, Microsoft.Maui.Controls.Label value)
    {
        var grid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }
        };
        grid.Add(Label(text, 14, false));
        grid.Add(value, 1);
        return grid;
    }

    private static string Format(decimal value) => value.ToString("C2", Gb);

    private void OnChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PaymentViewModel.Tendered) or nameof(PaymentViewModel.AmountDue))
        {
            // Keep keypad buffer aligned when Exact / quick chips change tendered.
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

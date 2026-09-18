using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Contracts.Dtos;
using OrderWeb.SharedUI.Controls;

namespace OrderWeb.SharedUI.Views;

public enum BarInventoryActionKind
{
    AddStock,
    Receive,
    Waste,
    CurrentStock,
    SuggestedOrder,
    WeeklyReport
}

public sealed record BarStockBoardRowPresentation(
    string StockId,
    string Name,
    string Unit,
    string OnHandDisplay,
    string Status,
    string Detail,
    string Section = "",
    string SuggestDisplay = "—",
    string SectionKey = "",
    string? Sku = null,
    decimal OnHand = 0m,
    decimal PackSize = 0m,
    decimal LowLevel = 0m,
    decimal MaxLevel = 0m,
    decimal SuggestOrderQty = 0m);

public sealed record BarStockWeeklyReportRowPresentation(
    string Name,
    string SectionDisplay,
    string HaveDisplay,
    string UsedDisplay,
    string WasteDisplay,
    string? Sku = null);

public sealed record BarInventoryReportRangeRequest(
    DateTime StartDate,
    DateTime EndDate,
    int? PresetDays);

/// <summary>
/// Host-neutral Bar Inventory workspace for Mother and Client POS.
/// Presentation only — hosts own data, Mother hub, and authorization.
/// </summary>
public sealed class BarInventoryView : ContentView
{
    private readonly Label _summaryLabel;
    private readonly Button _downloadPdfButton;
    private readonly Label _bannerLabel;
    private readonly Border _bannerBorder;
    private readonly Label _modeHeadingLabel;
    private readonly Border _modeHeadingBar;
    private readonly VerticalStackLayout _stockList;
    private readonly HorizontalStackLayout _sectionChipRow;
    private readonly HorizontalStackLayout _opsRow;
    private readonly Button _addStockAction;
    private readonly Entry _searchEntry;
    private readonly Dictionary<string, Border> _sectionChips = new(StringComparer.OrdinalIgnoreCase);
    private string _selectedSection = BarStockSections.All;
    private IReadOnlyList<BarStockBoardRowPresentation> _boardRows = Array.Empty<BarStockBoardRowPresentation>();
    private IReadOnlyList<BarStockWeeklyReportRowPresentation> _reportRows = Array.Empty<BarStockWeeklyReportRowPresentation>();
    private readonly VerticalStackLayout _reportToolbar;
    private readonly HorizontalStackLayout _reportPeriodRow;
    private readonly Grid _reportCustomRow;
    private readonly DatePicker _reportFromPicker;
    private readonly DatePicker _reportToPicker;
    private readonly Label _reportHaveHero;
    private readonly Label _reportUseHero;
    private readonly Label _reportWasteHero;
    private readonly Button _downloadCsvButton;
    private readonly Entry _reportSearchEntry;
    private readonly Dictionary<int, Border> _reportPeriodChips = new();
    private int? _reportPresetDays = 7;
    private bool _showingReport;
    private bool _suggestMode;
    private bool _showAdminCreate;
    private bool _showReport;
    private string _modeHeading = "Stock";
    private readonly View _reportAction;

    public BarInventoryView()
    {
        _summaryLabel = StrongLabel("No tracked items", 14);
        _downloadPdfButton = new Button
        {
            Text = "Download PDF",
            Style = null,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#0369A1"),
            FontFamily = "OpenSansSemibold",
            FontSize = 12,
            CornerRadius = 10,
            Padding = new Thickness(12, 6),
            HeightRequest = 34,
            MinimumHeightRequest = 34,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false
        };
        _downloadPdfButton.Clicked += (_, _) => SuggestPdfDownloadRequested?.Invoke(this, EventArgs.Empty);

        _reportFromPicker = new DatePicker
        {
            Date = DateTime.Today.AddDays(-6),
            Format = "d MMM yyyy",
            FontSize = 13,
            TextColor = Color.FromArgb("#0F172A"),
            VerticalOptions = LayoutOptions.Center
        };
        _reportToPicker = new DatePicker
        {
            Date = DateTime.Today,
            Format = "d MMM yyyy",
            FontSize = 13,
            TextColor = Color.FromArgb("#0F172A"),
            VerticalOptions = LayoutOptions.Center
        };
        _reportPeriodRow = new HorizontalStackLayout { Spacing = 8 };
        foreach (var days in new[] { 7, 15, 30, 365 })
        {
            var chip = BuildReportPeriodChip(days);
            _reportPeriodChips[days] = chip;
            _reportPeriodRow.Children.Add(chip);
        }

        var customChip = BuildReportPeriodChip(null, "Custom");
        _reportPeriodChips[0] = customChip;
        _reportPeriodRow.Children.Add(customChip);

        var applyCustom = new Button
        {
            Text = "Apply",
            Style = null,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#334155"),
            FontFamily = "OpenSansSemibold",
            FontSize = 12,
            CornerRadius = 10,
            Padding = new Thickness(12, 6),
            HeightRequest = 34,
            VerticalOptions = LayoutOptions.Center
        };
        applyCustom.Clicked += (_, _) =>
        {
            _reportPresetDays = null;
            ApplyReportPeriodChipStyles();
            ReportRangeRequested?.Invoke(this, new BarInventoryReportRangeRequest(
                ReportStartDate,
                ReportEndDate,
                null));
        };

        _reportCustomRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8,
            IsVisible = false,
            VerticalOptions = LayoutOptions.Center
        };
        _reportCustomRow.Add(MutedLabel("From", 12), 0);
        _reportCustomRow.Add(_reportFromPicker, 1);
        _reportCustomRow.Add(MutedLabel("To", 12), 2);
        _reportCustomRow.Add(_reportToPicker, 3);
        _reportCustomRow.Add(applyCustom, 4);

        _reportHaveHero = StrongLabel("—", 18);
        _reportUseHero = StrongLabel("—", 18);
        _reportWasteHero = StrongLabel("—", 18);
        var heroRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8
        };
        heroRow.Add(BuildReportHeroTile("Have", _reportHaveHero, "#0F766E", "#ECFDF5"), 0);
        heroRow.Add(BuildReportHeroTile("Use", _reportUseHero, "#0369A1", "#F0F9FF"), 1);
        heroRow.Add(BuildReportHeroTile("Waste", _reportWasteHero, "#BE185D", "#FDF2F8"), 2);

        _downloadCsvButton = new Button
        {
            Text = "Download CSV",
            Style = null,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#334155"),
            FontFamily = "OpenSansSemibold",
            FontSize = 12,
            CornerRadius = 10,
            Padding = new Thickness(12, 6),
            HeightRequest = 34,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };
        _downloadCsvButton.Clicked += (_, _) => ReportCsvDownloadRequested?.Invoke(this, EventArgs.Empty);

        _reportSearchEntry = new Entry
        {
            Placeholder = "Search report…",
            FontSize = 14,
            TextColor = Color.FromArgb("#0F172A"),
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing
        };
        _reportSearchEntry.TextChanged += (_, _) =>
        {
            if (_showingReport)
            {
                PaintReportRows();
            }
        };
        var reportSearchBox = new Border
        {
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#E2E8F0"),
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(12, 0),
            HeightRequest = 40,
            Content = _reportSearchEntry
        };

        _reportToolbar = new VerticalStackLayout
        {
            Spacing = 10,
            IsVisible = false,
            Children =
            {
                new ScrollView
                {
                    Orientation = ScrollOrientation.Horizontal,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
                    Content = _reportPeriodRow
                },
                _reportCustomRow,
                heroRow,
                new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(GridLength.Auto)
                    },
                    ColumnSpacing = 10,
                    Children =
                    {
                        reportSearchBox,
                    }
                }
            }
        };
        ((Grid)_reportToolbar.Children[^1]).Add(_downloadCsvButton, 1);

        _bannerLabel = new Label
        {
            FontSize = 12,
            TextColor = Color.FromArgb("#92400E"),
            LineBreakMode = LineBreakMode.WordWrap
        };
        _bannerBorder = new Border
        {
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#FCD34D"),
            BackgroundColor = Color.FromArgb("#FFFBEB"),
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 8),
            IsVisible = false,
            Content = _bannerLabel
        };
        _stockList = new VerticalStackLayout { Spacing = 4 };
        _sectionChipRow = new HorizontalStackLayout { Spacing = 8 };
        RebuildSectionChips(Array.Empty<string>());

        _searchEntry = new Entry
        {
            Placeholder = "Search stock…",
            FontSize = 14,
            TextColor = Color.FromArgb("#0F172A"),
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
            ClearButtonVisibility = ClearButtonVisibility.WhileEditing
        };
        _searchEntry.TextChanged += (_, _) =>
        {
            if (_showingReport)
            {
                return;
            }

            PaintBoardRows();
        };

        var searchBox = new Border
        {
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#E2E8F0"),
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(12, 0),
            HeightRequest = 44,
            MinimumWidthRequest = 220,
            Content = _searchEntry
        };

        // Real Button — Border+Tap next to Search was focusing Search keyboard instead.
        _addStockAction = new Button
        {
            Text = "Add stock",
            Style = null,
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#2563EB"),
            FontFamily = "OpenSansSemibold",
            FontSize = 14,
            CornerRadius = 12,
            Padding = new Thickness(16, 8),
            HeightRequest = 44,
            MinimumWidthRequest = 120,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false
        };
        _addStockAction.Clicked += (_, _) =>
        {
            UnfocusSearch();
            SharedTouchKeyboard.SuppressAllBriefly(600);
            ActionRequested?.Invoke(this, BarInventoryActionKind.AddStock);
        };

        var searchCluster = new HorizontalStackLayout
        {
            Spacing = 10,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Children = { _addStockAction, searchBox }
        };

        // Title only — categories + ops sit on the next row.
        var titleRow = StrongLabel("Bar Inventory", 32);

        _opsRow = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
            Children =
            {
                CompactAction("Waste", BarInventoryActionKind.Waste, "#BE185D", "#FDF2F8"),
                CompactAction("Order", BarInventoryActionKind.SuggestedOrder, "#0369A1", "#F0F9FF"),
            }
        };
        _reportAction = CompactAction("Report", BarInventoryActionKind.WeeklyReport, "#6D28D9", "#F5F3FF");
        _reportAction.IsVisible = false;
        _opsRow.Children.Add(_reportAction);

        var rightTools = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
            Children = { searchCluster, _opsRow }
        };

        var filterOpsRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            VerticalOptions = LayoutOptions.Center
        };
        filterOpsRow.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalOptions = LayoutOptions.Center,
            Content = _sectionChipRow
        }, 0);
        filterOpsRow.Add(rightTools, 1);

        _modeHeadingLabel = new Label
        {
            Text = _modeHeading,
            FontFamily = "OpenSansSemibold",
            FontSize = 16,
            TextColor = Color.FromArgb("#0F172A"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        _modeHeadingBar = new Border
        {
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#E2E8F0"),
            BackgroundColor = Color.FromArgb("#F1F5F9"),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(16, 10),
            Content = _modeHeadingLabel
        };

        var summaryRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10,
            VerticalOptions = LayoutOptions.Center
        };
        summaryRow.Add(_summaryLabel, 0);
        summaryRow.Add(_downloadPdfButton, 1);

        var stockCard = new Border
        {
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#D8E1ED"),
            BackgroundColor = Colors.White,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(10, 8),
            VerticalOptions = LayoutOptions.Start,
            Content = new VerticalStackLayout
            {
                Spacing = 6,
                Children =
                {
                    summaryRow,
                    _reportToolbar,
                    _stockList
                }
            }
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(20, 16, 20, 24),
                Spacing = 12,
                Children =
                {
                    titleRow,
                    filterOpsRow,
                    _modeHeadingBar,
                    _bannerBorder,
                    stockCard
                }
            }
        };
    }

    public event EventHandler<BarInventoryActionKind>? ActionRequested;
    public event EventHandler<string>? SectionChanged;
    public event EventHandler<BarStockBoardRowPresentation>? QuickAddRequested;
    public event EventHandler<BarStockBoardRowPresentation>? EditRequested;
    public event EventHandler? SuggestPdfDownloadRequested;
    public event EventHandler<BarInventoryReportRangeRequest>? ReportRangeRequested;
    public event EventHandler? ReportCsvDownloadRequested;

    public bool IsSuggestMode => _suggestMode;
    public bool IsReportMode => _showingReport;
    public DateTime ReportStartDate => (_reportFromPicker.Date ?? DateTime.Today.AddDays(-6)).Date;
    public DateTime ReportEndDate => (_reportToPicker.Date ?? DateTime.Today).Date;
    public int? ReportPresetDays => _reportPresetDays;

    public string SelectedSection => _selectedSection;

    public string SearchText => (_searchEntry.Text ?? string.Empty).Trim();

    public void UnfocusSearch()
    {
        try
        {
            _searchEntry.Unfocus();
        }
        catch
        {
            // Ignore focus race.
        }
    }

    /// <summary>Admin-only Add stock button on Mother Bar Inventory.</summary>
    public bool ShowAdminCreate
    {
        get => _showAdminCreate;
        set
        {
            _showAdminCreate = value;
            _addStockAction.IsVisible = value;
            if (!_showingReport && _boardRows.Count > 0)
            {
                PaintBoardRows();
            }
        }
    }

    /// <summary>Admin-only Stock report (Have/Use/Waste + CSV). Hidden for Bar Manager.</summary>
    public bool ShowReport
    {
        get => _showReport;
        set
        {
            _showReport = value;
            _reportAction.IsVisible = value;
            if (!value && _showingReport)
            {
                SetBoard(_boardRows, suggestMode: false);
            }
        }
    }

    public void SetSelectedSection(string? section, bool raiseEvent = false)
    {
        var normalized = BarStockSections.NormalizeFilter(section);
        _selectedSection = normalized;
        if (_selectedSection != BarStockSections.All)
        {
            EnsureSectionChip(_selectedSection);
        }

        ApplySectionChipStyles();
        if (raiseEvent)
        {
            SectionChanged?.Invoke(this, _selectedSection);
        }
    }

    /// <summary>Refresh category chips from board data (presets + Admin-typed customs).</summary>
    public void SetSectionChips(IEnumerable<string>? sectionKeys)
    {
        var extras = (sectionKeys ?? Array.Empty<string>())
            .Select(BarStockSections.Normalize)
            .Where(k => !string.IsNullOrWhiteSpace(k) && k != BarStockSections.All)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        RebuildSectionChips(extras);
        if (_selectedSection != BarStockSections.All)
        {
            EnsureSectionChip(_selectedSection);
        }

        ApplySectionChipStyles();
    }

    /// <summary>Quiet status — gray mode·section bar removed; use mode heading instead.</summary>
    public void SetStatus(string message)
    {
        _ = message;
        // Non-advisory status bar hidden (replaced by centered mode heading).
    }

    /// <summary>Soft amber Track Off drinks note. Null/empty hides advisory.</summary>
    public void SetAdvisoryNote(string? note)
    {
        var text = note?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            _bannerBorder.IsVisible = false;
            _bannerLabel.Text = string.Empty;
            return;
        }

        SetBanner(text, advisory: true);
    }

    private void SetBanner(string? message, bool advisory)
    {
        var text = message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) || !advisory)
        {
            if (!advisory)
            {
                return;
            }

            _bannerBorder.IsVisible = false;
            _bannerLabel.Text = string.Empty;
            return;
        }

        _bannerLabel.Text = text;
        _bannerLabel.TextColor = Color.FromArgb("#92400E");
        _bannerBorder.BackgroundColor = Color.FromArgb("#FFFBEB");
        _bannerBorder.Stroke = Color.FromArgb("#FCD34D");
        _bannerBorder.IsVisible = true;
    }

    public void SetBoard(IReadOnlyList<BarStockBoardRowPresentation> rows, string? boardTitle = null, bool suggestMode = false)
    {
        _ = boardTitle;
        _showingReport = false;
        _suggestMode = suggestMode;
        _downloadPdfButton.IsVisible = suggestMode;
        _reportToolbar.IsVisible = false;
        _boardRows = rows ?? Array.Empty<BarStockBoardRowPresentation>();
        SetModeHeading(suggestMode ? "Order" : "Stock");
        PaintBoardRows();
    }

    /// <summary>Have / Use / Waste period report (presets + custom + CSV).</summary>
    public void SetUsageReport(
        string periodLabel,
        string haveTotalDisplay,
        string useTotalDisplay,
        string wasteTotalDisplay,
        DateTime startDate,
        DateTime endDate,
        int? presetDays,
        IReadOnlyList<BarStockWeeklyReportRowPresentation> rows)
    {
        _showingReport = true;
        _suggestMode = false;
        _downloadPdfButton.IsVisible = false;
        _reportToolbar.IsVisible = true;
        _reportPresetDays = presetDays;
        _reportFromPicker.Date = startDate.Date;
        _reportToPicker.Date = endDate.Date;
        _reportCustomRow.IsVisible = presetDays is null;
        ApplyReportPeriodChipStyles();
        SetModeHeading("Report");

        _summaryLabel.Text = string.IsNullOrWhiteSpace(periodLabel) ? "Stock report" : periodLabel.Trim();
        _reportHaveHero.Text = string.IsNullOrWhiteSpace(haveTotalDisplay) ? "—" : haveTotalDisplay.Trim();
        _reportUseHero.Text = string.IsNullOrWhiteSpace(useTotalDisplay) ? "—" : useTotalDisplay.Trim();
        _reportWasteHero.Text = string.IsNullOrWhiteSpace(wasteTotalDisplay) ? "—" : wasteTotalDisplay.Trim();
        _reportRows = rows ?? Array.Empty<BarStockWeeklyReportRowPresentation>();
        PaintReportRows();
    }

    /// <summary>Legacy name — prefer <see cref="SetUsageReport"/>.</summary>
    public void SetWeeklyReport(
        string periodLabel,
        string totalsLine,
        IReadOnlyList<BarStockWeeklyReportRowPresentation> rows,
        string? supplierFooter = null)
    {
        _ = totalsLine;
        _ = supplierFooter;
        SetUsageReport(
            periodLabel,
            "—",
            "—",
            "—",
            DateTime.Today.AddDays(-6),
            DateTime.Today,
            7,
            rows);
    }

    /// <summary>Legacy helper used by early hosts; prefer <see cref="SetBoard"/>.</summary>
    public void SetStockSummary(string title, IEnumerable<(string Title, string Detail)>? rows = null)
    {
        var list = new List<BarStockBoardRowPresentation>();
        if (rows is not null)
        {
            foreach (var row in rows)
            {
                list.Add(new BarStockBoardRowPresentation("", row.Title, "", "—", "OK", row.Detail));
            }
        }

        SetBoard(list);
        if (!string.IsNullOrWhiteSpace(title))
        {
            _summaryLabel.Text = title.Trim();
        }
    }

    private void PaintBoardRows()
    {
        _stockList.Children.Clear();
        var isHome = _selectedSection == BarStockSections.All;
        var sectionLabel = isHome ? "stock" : BarStockSections.DisplayName(_selectedSection);
        var query = SearchText;
        var rows = string.IsNullOrWhiteSpace(query)
            ? _boardRows
            : _boardRows
                .Where(r =>
                    r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    r.Detail.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    r.OnHandDisplay.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (rows.Count == 0)
        {
            _summaryLabel.Text = string.IsNullOrWhiteSpace(query)
                ? (_suggestMode
                    ? (isHome ? "No items to order" : $"No {sectionLabel} items to order")
                    : (isHome ? "No tracked items" : $"No {sectionLabel} items"))
                : "No matches";
            _stockList.Children.Add(MutedLabel(
                string.IsNullOrWhiteSpace(query)
                    ? (_suggestMode
                        ? (isHome
                            ? "Nothing at/under Low — or Max already reached."
                            : $"Nothing at/under Low in {sectionLabel} — or Max already reached.")
                        : (isHome
                            ? "No tracked items yet — Admin can tap Add stock (or enable Track on Add Item)."
                            : $"No {sectionLabel} tracked yet — Admin can tap Add stock (or enable Track on Add Item)."))
                    : $"No stock matching “{query}”.",
                14));
            return;
        }

        _summaryLabel.Text = _suggestMode
            ? (rows.Count == 1
                ? (isHome ? "Order 1 item" : $"{sectionLabel} · order 1 item")
                : (isHome ? $"Order {rows.Count} items" : $"{sectionLabel} · order {rows.Count} items"))
            : (rows.Count == 1
                ? (isHome ? "1 item" : $"{sectionLabel} · 1 item")
                : (isHome ? $"{rows.Count} items" : $"{sectionLabel} · {rows.Count} items"));

        foreach (var row in rows)
        {
            _stockList.Children.Add(_suggestMode ? BuildSuggestCard(row) : BuildStockCard(row));
        }
    }

    private void PaintReportRows()
    {
        _stockList.Children.Clear();
        var query = (_reportSearchEntry.Text ?? string.Empty).Trim();
        var rows = string.IsNullOrWhiteSpace(query)
            ? _reportRows
            : _reportRows
                .Where(r =>
                    r.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (r.Sku?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    r.SectionDisplay.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    r.HaveDisplay.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        if (rows.Count == 0)
        {
            _stockList.Children.Add(MutedLabel(
                string.IsNullOrWhiteSpace(query)
                    ? "No stock rows for this period"
                    : "No matches",
                14));
            return;
        }

        string? lastSection = null;
        foreach (var row in rows)
        {
            if (!string.Equals(lastSection, row.SectionDisplay, StringComparison.OrdinalIgnoreCase))
            {
                lastSection = row.SectionDisplay;
                _stockList.Children.Add(new Label
                {
                    Text = row.SectionDisplay,
                    FontFamily = "OpenSansSemibold",
                    FontSize = 13,
                    TextColor = Color.FromArgb("#0F766E"),
                    Margin = new Thickness(0, 8, 0, 2)
                });
            }

            _stockList.Children.Add(new Border
            {
                StrokeThickness = 1,
                Stroke = Color.FromArgb("#E2E8F0"),
                BackgroundColor = Colors.White,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Padding = new Thickness(12, 10),
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        StrongLabel(row.Name, 14),
                        string.IsNullOrWhiteSpace(row.Sku)
                            ? MutedLabel($"Have {row.HaveDisplay}", 12)
                            : MutedLabel($"{row.Sku} · Have {row.HaveDisplay}", 12),
                        MutedLabel($"Use {row.UsedDisplay} · Waste {row.WasteDisplay}", 12)
                    }
                }
            });
        }
    }

    private Border BuildReportPeriodChip(int? days, string? customLabel = null)
    {
        var label = customLabel ?? $"{days}d";
        var text = new Label
        {
            Text = label,
            FontFamily = "OpenSansSemibold",
            FontSize = 12,
            TextColor = Color.FromArgb("#334155"),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };
        var chip = new Border
        {
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#E2E8F0"),
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(12, 6),
            Content = text
        };
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (days is null)
            {
                _reportPresetDays = null;
                _reportCustomRow.IsVisible = true;
                ApplyReportPeriodChipStyles();
                return;
            }

            _reportPresetDays = days;
            _reportCustomRow.IsVisible = false;
            var end = DateTime.Today;
            var start = end.AddDays(-(days.Value - 1));
            _reportFromPicker.Date = start;
            _reportToPicker.Date = end;
            ApplyReportPeriodChipStyles();
            ReportRangeRequested?.Invoke(this, new BarInventoryReportRangeRequest(start, end, days));
        };
        chip.GestureRecognizers.Add(tap);
        return chip;
    }

    private void ApplyReportPeriodChipStyles()
    {
        foreach (var (key, chip) in _reportPeriodChips)
        {
            var selected = _reportPresetDays is null
                ? key == 0
                : key == _reportPresetDays.Value;
            var label = chip.Content as Label;
            if (label is not null)
            {
                label.TextColor = selected ? Colors.White : Color.FromArgb("#334155");
            }

            chip.BackgroundColor = selected
                ? Color.FromArgb("#334155")
                : Color.FromArgb("#F8FAFC");
            chip.Stroke = selected
                ? Color.FromArgb("#334155")
                : Color.FromArgb("#E2E8F0");
        }
    }

    private static View BuildReportHeroTile(string title, Label valueLabel, string accent, string bg)
    {
        return new Border
        {
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#E2E8F0"),
            BackgroundColor = Color.FromArgb(bg),
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(10, 8),
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label
                    {
                        Text = title,
                        FontSize = 11,
                        FontFamily = "OpenSansSemibold",
                        TextColor = Color.FromArgb(accent)
                    },
                    valueLabel
                }
            }
        };
    }

    private View BuildSuggestCard(BarStockBoardRowPresentation row)
    {
        var orderText = string.IsNullOrWhiteSpace(row.SuggestDisplay) || row.SuggestDisplay == "—"
            ? "Order —"
            : $"Order {row.SuggestDisplay}";
        var nowLine = FormatStockLeftLine(row);
        var meta = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.Sku))
        {
            meta.Add(row.Sku!);
        }

        if (row.LowLevel > 0 || row.MaxLevel > 0)
        {
            meta.Add($"low {FormatOnHandQty(row.LowLevel)} · max {FormatOnHandQty(row.MaxLevel)}");
        }

        var body = new VerticalStackLayout
        {
            Spacing = 1,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                StrongLabel(row.Name, 14),
                new Label
                {
                    Text = orderText,
                    FontSize = 15,
                    FontFamily = "OpenSansSemibold",
                    TextColor = Color.FromArgb("#0369A1"),
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 1
                },
                MutedLabel($"Now {nowLine}" + (meta.Count == 0 ? string.Empty : " · " + string.Join(" · ", meta)), 11)
            }
        };

        var rowGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10,
            VerticalOptions = LayoutOptions.Center
        };
        rowGrid.Add(body, 0);
        rowGrid.Add(
            CardButton("Add", Colors.White, Color.FromArgb("#047857"),
                () => QuickAddRequested?.Invoke(this, row)),
            1);

        return new Border
        {
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#BAE6FD"),
            BackgroundColor = Color.FromArgb("#F0F9FF"),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(10, 6),
            Content = rowGrid
        };
    }

    private View BuildStockCard(BarStockBoardRowPresentation row)
    {
        var detailParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.Detail))
        {
            // Pack size shown in the stock line as total ml — skip "N ml/btl" noise.
            foreach (var part in row.Detail.Split(" · ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (part.Contains("ml/btl", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                detailParts.Add(part);
            }
        }

        if (!string.IsNullOrWhiteSpace(row.SuggestDisplay) &&
            row.SuggestDisplay != "—" &&
            string.Equals(row.Status, "Low", StringComparison.OrdinalIgnoreCase))
        {
            detailParts.Add($"Order {row.SuggestDisplay}");
        }

        var isLow = string.Equals(row.Status, "Low", StringComparison.OrdinalIgnoreCase);
        var stockLine = FormatStockLeftLine(row);
        var body = new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                StrongLabel(row.Name, 13),
                new Label
                {
                    Text = stockLine,
                    FontSize = 12,
                    FontFamily = "OpenSansSemibold",
                    TextColor = Color.FromArgb(isLow ? "#C2410C" : "#1D4ED8"),
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 1
                }
            }
        };
        if (detailParts.Count > 0)
        {
            body.Children.Add(MutedLabel(string.Join(" · ", detailParts), 10));
        }

        var actions = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                CardButton("+ Stock", Colors.White, Color.FromArgb("#2563EB"), () => QuickAddRequested?.Invoke(this, row))
            }
        };
        if (_showAdminCreate)
        {
            actions.Children.Add(
                CardButton("Edit", Color.FromArgb("#1D4ED8"), Color.FromArgb("#EFF6FF"), () => EditRequested?.Invoke(this, row)));
        }

        var grid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 8
        };
        grid.Add(body, 0);
        grid.Add(actions, 1);
        if (isLow)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            grid.Add(new Label
            {
                Text = "Low",
                FontFamily = "OpenSansSemibold",
                FontSize = 11,
                TextColor = Color.FromArgb("#C2410C"),
                VerticalOptions = LayoutOptions.Center
            }, 2);
        }

        return new Border
        {
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#E2E8F0"),
            BackgroundColor = Color.FromArgb("#F8FAFC"),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(10, 4),
            Content = grid
        };
    }

    private static string FormatStockLeftLine(BarStockBoardRowPresentation row)
    {
        var qty = FormatOnHandQty(row.OnHand);
        var unit = FormatStockUnitLabel(row.Unit, row.OnHand);
        if (row.PackSize > 0 &&
            string.Equals(BarStockUnits.Normalize(row.Unit), BarStockUnits.Bottle, StringComparison.OrdinalIgnoreCase))
        {
            var totalMl = row.OnHand * row.PackSize;
            return $"{qty} {unit} · {FormatOnHandQty(totalMl)} ml";
        }

        return $"{qty} {unit}";
    }

    private static string FormatOnHandQty(decimal onHand)
    {
        if (onHand == decimal.Truncate(onHand))
        {
            return onHand.ToString("0");
        }

        return onHand.ToString("0.###");
    }

    private static string FormatStockUnitLabel(string? unit, decimal onHand)
    {
        var raw = string.IsNullOrWhiteSpace(unit) ? "unit" : unit.Trim();
        if (string.Equals(raw, "bottle", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Abs(onHand) == 1m ? "bottle" : "bottles";
        }

        if (string.Equals(raw, "case", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Abs(onHand) == 1m ? "case" : "cases";
        }

        return raw;
    }

    private static Button CardButton(string title, Color textColor, Color background, Action onTap)
    {
        var button = new Button
        {
            Text = title,
            Style = null,
            TextColor = textColor,
            BackgroundColor = background,
            FontFamily = "OpenSansSemibold",
            FontSize = 11,
            CornerRadius = 8,
            Padding = new Thickness(10, 2),
            HeightRequest = 28,
            MinimumHeightRequest = 28,
            VerticalOptions = LayoutOptions.Center
        };
        button.Clicked += (_, _) => onTap();
        return button;
    }

    private void RebuildSectionChips(IReadOnlyList<string> extraKeys)
    {
        _sectionChipRow.Children.Clear();
        _sectionChips.Clear();

        // Home first — all stock items (1st page).
        var homeChip = BuildSectionChip(BarStockSections.All, "Home");
        _sectionChips[BarStockSections.All] = homeChip;
        _sectionChipRow.Children.Add(homeChip);

        foreach (var (key, label) in BarStockSections.PresetChips)
        {
            var chip = BuildSectionChip(key, label);
            _sectionChips[key] = chip;
            _sectionChipRow.Children.Add(chip);
        }

        foreach (var key in extraKeys
                     .Where(k => !BarStockSections.IsPreset(k) &&
                                 !string.Equals(k, BarStockSections.All, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(BarStockSections.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            if (_sectionChips.ContainsKey(key))
            {
                continue;
            }

            var chip = BuildSectionChip(key, BarStockSections.DisplayName(key));
            _sectionChips[key] = chip;
            _sectionChipRow.Children.Add(chip);
        }

        ApplySectionChipStyles();
    }

    private void EnsureSectionChip(string key)
    {
        if (string.Equals(key, BarStockSections.All, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var normalized = BarStockSections.Normalize(key);
        if (_sectionChips.ContainsKey(normalized))
        {
            return;
        }

        var chip = BuildSectionChip(normalized, BarStockSections.DisplayName(normalized));
        _sectionChips[normalized] = chip;
        _sectionChipRow.Children.Add(chip);
    }

    private Border BuildSectionChip(string key, string label)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            var isHome = string.Equals(key, BarStockSections.All, StringComparison.OrdinalIgnoreCase);
            if (string.Equals(_selectedSection, key, StringComparison.OrdinalIgnoreCase) &&
                !isHome)
            {
                return;
            }

            SetSelectedSection(key, raiseEvent: true);
            if (isHome)
            {
                SetModeHeading("Stock");
                if (_showingReport || _suggestMode)
                {
                    ActionRequested?.Invoke(this, BarInventoryActionKind.CurrentStock);
                }
            }
        };

        var border = new Border
        {
            StrokeThickness = 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 20 },
            Padding = new Thickness(14, 8),
            Content = new Label
            {
                Text = label,
                FontFamily = "OpenSansSemibold",
                FontSize = 13,
                HorizontalTextAlignment = TextAlignment.Center
            }
        };
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private void ApplySectionChipStyles()
    {
        foreach (var (key, chip) in _sectionChips)
        {
            var selected = string.Equals(key, _selectedSection, StringComparison.OrdinalIgnoreCase);
            chip.BackgroundColor = Color.FromArgb(selected ? "#0F766E" : "#FFFFFF");
            chip.Stroke = Color.FromArgb(selected ? "#0F766E" : "#CBD5E1");
            if (chip.Content is Label label)
            {
                label.TextColor = Color.FromArgb(selected ? "#FFFFFF" : "#334155");
            }
        }
    }

    private void SetModeHeading(string title)
    {
        _modeHeading = string.IsNullOrWhiteSpace(title) ? "Stock" : title.Trim();
        _modeHeadingLabel.Text = _modeHeading;
    }

    private View CompactAction(string title, BarInventoryActionKind action, string accent, string background)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (action is BarInventoryActionKind.WeeklyReport && !_showReport)
            {
                return;
            }

            SetModeHeading(action switch
            {
                BarInventoryActionKind.Receive => "Receive",
                BarInventoryActionKind.Waste => "Waste",
                BarInventoryActionKind.SuggestedOrder => "Order",
                BarInventoryActionKind.WeeklyReport => "Report",
                BarInventoryActionKind.CurrentStock => "Stock",
                _ => title
            });
            ActionRequested?.Invoke(this, action);
        };

        var border = new Border
        {
            StrokeThickness = 1,
            Stroke = Color.FromArgb("#E2E8F0"),
            BackgroundColor = Color.FromArgb(background),
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(14, 10),
            Content = new Label
            {
                Text = title,
                FontFamily = "OpenSansSemibold",
                FontSize = 13,
                TextColor = Color.FromArgb(accent)
            }
        };
        border.GestureRecognizers.Add(tap);
        return border;
    }

    private static Label StrongLabel(string text, double size) =>
        new()
        {
            Text = text,
            FontFamily = "OpenSansSemibold",
            FontSize = size,
            TextColor = Color.FromArgb("#0F172A")
        };

    private static Label MutedLabel(string text, double size) =>
        new()
        {
            Text = text,
            FontFamily = "OpenSansRegular",
            FontSize = size,
            TextColor = Color.FromArgb("#64748B"),
            LineBreakMode = LineBreakMode.WordWrap
        };
}

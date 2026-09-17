using OrderWeb.Client.Services;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Features;
using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.Views;

namespace OrderWeb.Client.Pages.Manager;

public partial class BarInventoryPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private readonly MotherBarInventoryClient _barInventory = new();
    private readonly Dictionary<string, string> _stickyKeys = new(StringComparer.Ordinal);
    private bool _actionBusy;
    private IReadOnlyList<BarStockBoardRowDto> _allBoard = Array.Empty<BarStockBoardRowDto>();
    private CancellationTokenSource? _quietPullCts;
    private BarStockWeeklyReportResponseDto? _lastReport;
    private DateTime _reportStart = DateTime.Today.AddDays(-6);
    private DateTime _reportEnd = DateTime.Today;
    private int? _reportPresetDays = 7;

    public BarInventoryPage()
    {
        InitializeComponent();

        TopBar.SetPageTitle("Bar Inventory");
        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await ClientSignOut.RequestAsync(this);
        Sidebar.MenuItemSelected += async (_, menu) => await ClientSidebarNavigation.SwitchAsync(this, menu, "inventory");

        Inventory.ActionRequested += OnInventoryActionRequested;
        Inventory.SectionChanged += (_, _) =>
        {
            if (Inventory.IsReportMode)
            {
                PaintCachedReport();
            }
            else
            {
                ApplyFilteredBoard(suggestedOnly: Inventory.IsSuggestMode);
            }
        };
        Inventory.SuggestPdfDownloadRequested += async (_, _) => await ExportSuggestPdfAsync();
        Inventory.ReportRangeRequested += async (_, range) =>
            await ShowUsageReportAsync(range.StartDate, range.EndDate, range.PresetDays);
        Inventory.ReportCsvDownloadRequested += async (_, _) => await ExportUsageReportCsvAsync();
        Inventory.QuickAddRequested += async (_, row) =>
        {
            if (_actionBusy)
            {
                return;
            }

            try
            {
                _actionBusy = true;
                await RunQuickAddAsync(row);
            }
            finally
            {
                _actionBusy = false;
            }
        };
        Inventory.EditRequested += async (_, _) =>
            await DisplayAlert("Bar Inventory", "Edit stock is Admin-only on Mother POS.", "OK");
        Inventory.ShowAdminCreate = false;
        Inventory.ShowReport = false;
        Inventory.SetBoard(Array.Empty<BarStockBoardRowPresentation>());
        MotherEventClient.SharedAuthoritativeDataChanged += OnMotherDataChanged;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        MotherEventClient.SharedAuthoritativeDataChanged -= OnMotherDataChanged;
        StopQuietPull();
    }

    private void OnMotherDataChanged(object? sender, MotherDataChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.EventType) ||
            !e.EventType.Contains("barinventory", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _ = QuietReloadBoardAsync();
    }

    private async Task QuietReloadBoardAsync()
    {
        try
        {
            await LoadBoardAsync(quiet: true);
        }
        catch
        {
            // Ignore quiet refresh failures.
        }
    }

    private void StartQuietPull()
    {
        StopQuietPull();
        _quietPullCts = new CancellationTokenSource();
        var token = _quietPullCts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(45), token);
                    await MainThread.InvokeOnMainThreadAsync(() => LoadBoardAsync(quiet: true));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Keep looping quietly.
                }
            }
        }, token);
    }

    private void StopQuietPull()
    {
        try
        {
            _quietPullCts?.Cancel();
            _quietPullCts?.Dispose();
        }
        catch
        {
            // Ignore.
        }

        _quietPullCts = null;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAccessFromMotherAsync();
        if (!HasBarInventoryAccess())
        {
            await DisplayAlert(
                "Bar Inventory",
                "This till cannot open Bar Inventory. On Mother: create a Bar Manager user, and turn Terminal Access → Bar Inventory ON, then Update All.",
                "OK");
            await Navigation.PopAsync(false);
            return;
        }

        var session = await _cache.GetCurrentLoginSessionAsync();
        var who = string.Equals(session?.Role, "BarManager", StringComparison.OrdinalIgnoreCase)
            ? "Bar Manager"
            : session?.Role ?? "Staff";
        Inventory.SetStatus($"{who} · online via Mother · shared stock workspace");
        await LoadBoardAsync();
        StartQuietPull();
    }

    private async Task LoadBoardAsync(bool quiet = false, bool keepSuggestMode = false)
    {
        var result = await _barInventory.ListBoardAsync();
        if (!result.Success)
        {
            if (!quiet)
            {
                Inventory.SetStatus(result.Message ?? "Could not load stock board.");
            }

            if (!result.FromCache)
            {
                _allBoard = Array.Empty<BarStockBoardRowDto>();
                Inventory.SetBoard(Array.Empty<BarStockBoardRowPresentation>());
                Inventory.SetAdvisoryNote(null);
            }

            return;
        }

        _allBoard = result.Items ?? Array.Empty<BarStockBoardRowDto>();
        Inventory.SetSectionChips(_allBoard.Select(r => r.Section));
        ApplyFilteredBoard(suggestedOnly: keepSuggestMode && Inventory.IsSuggestMode);
        if (result.FromCache)
        {
            Inventory.SetStatus(result.Message ?? "Mother offline · showing last saved stock");
        }
        else if (!quiet)
        {
            Inventory.SetStatus($"{_allBoard.Count} tracked stock item(s)");
        }

        // Bar Manager workspace — no Track On soft note (Admin-only on Mother).
        Inventory.SetAdvisoryNote(null);
    }

    private void ApplyFilteredBoard(bool suggestedOnly)
    {
        var section = Inventory.SelectedSection;
        var filtered = FilterBySection(_allBoard, section);
        if (suggestedOnly)
        {
            filtered = filtered
                .Where(r =>
                    string.Equals(r.Status, "Low", StringComparison.OrdinalIgnoreCase) &&
                    r.SuggestOrderQty > 0m)
                .ToList();
            Inventory.SetStatus(filtered.Count == 0
                ? $"Order · {BarStockSections.DisplayName(section)} · nothing to order"
                : $"Order · {BarStockSections.DisplayName(section)} · {filtered.Count} item(s) to order");
        }

        Inventory.SetBoard(MapBoard(filtered), suggestMode: suggestedOnly);
    }

    private static IReadOnlyList<BarStockBoardRowDto> FilterBySection(
        IReadOnlyList<BarStockBoardRowDto> rows,
        string section)
    {
        var filter = BarStockSections.NormalizeFilter(section);
        if (filter == BarStockSections.All)
        {
            return rows;
        }

        return rows
            .Where(r => string.Equals(BarStockSections.Normalize(r.Section), filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static List<BarStockBoardRowPresentation> MapBoard(IEnumerable<BarStockBoardRowDto> rows) =>
        rows.Select(r =>
        {
            var detailParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(r.Sku))
            {
                detailParts.Add(r.Sku!);
            }

            if (r.PackSize > 0 && string.Equals(r.StockUnit, BarStockUnits.Bottle, StringComparison.OrdinalIgnoreCase))
            {
                detailParts.Add($"{r.PackSize:0.###} ml/btl");
            }

            if (r.ParLevel > 0 || r.MaxLevel > 0 || r.LowLevel > 0)
            {
                var low = r.LowLevel > 0 ? r.LowLevel : 0m;
                var max = r.MaxLevel > 0 ? r.MaxLevel : r.ParLevel;
                detailParts.Add($"low {low:0.###} · max {max:0.###}");
            }

            detailParts.Add($"{r.LinkedMenuItemCount} menu link(s)");
            return new BarStockBoardRowPresentation(
                r.StockId,
                r.Name,
                r.StockUnit,
                $"{r.OnHand:0.###} {r.StockUnit}",
                r.Status,
                string.Join(" · ", detailParts),
                BarStockSections.DisplayName(r.Section),
                r.SuggestOrderDisplay,
                BarStockSections.Normalize(r.Section),
                r.Sku,
                r.OnHand,
                r.PackSize,
                r.LowLevel > 0 ? r.LowLevel : 0m,
                r.MaxLevel > 0 ? r.MaxLevel : r.ParLevel,
                r.SuggestOrderQty);
        }).ToList();

    private async void OnInventoryActionRequested(object? sender, BarInventoryActionKind action)
    {
        if (_actionBusy)
        {
            return;
        }

        try
        {
            _actionBusy = true;
            if (action is BarInventoryActionKind.AddStock)
            {
                await DisplayAlert("Bar Inventory", "Add stock is Admin-only on Mother POS.", "OK");
                return;
            }

            if (action is BarInventoryActionKind.CurrentStock)
            {
                Inventory.SetStatus("Current Stock · tracked items");
                await LoadBoardAsync();
                return;
            }

            if (action is BarInventoryActionKind.SuggestedOrder)
            {
                if (_allBoard.Count == 0)
                {
                    await LoadBoardAsync();
                }

                ApplyFilteredBoard(suggestedOnly: true);
                return;
            }

            if (action is BarInventoryActionKind.WeeklyReport)
            {
                await DisplayAlert(
                    "Stock report",
                    "Stock report is Admin-only on Mother POS. Bar Manager can Waste and Order.",
                    "OK");
                return;
            }

            var kind = action switch
            {
                BarInventoryActionKind.Receive => BarStockMovementKind.Receive,
                BarInventoryActionKind.Waste => BarStockMovementKind.Waste,
                _ => (BarStockMovementKind?)null
            };
            if (kind is null)
            {
                return;
            }

            await RunMovementAsync(kind.Value);
        }
        finally
        {
            _actionBusy = false;
        }
    }

    private async Task ShowUsageReportAsync(DateTime startDate, DateTime endDate, int? presetDays)
    {
        if (endDate.Date < startDate.Date)
        {
            Inventory.SetStatus("End date cannot be before start date.");
            await DisplayAlert("Stock report", "End date cannot be before start date.", "OK");
            return;
        }

        var report = await _barInventory.GetUsageReportAsync(startDate.Date, endDate.Date, presetDays);
        if (!report.Success)
        {
            Inventory.SetStatus(report.Message ?? "Could not load stock report.");
            return;
        }

        _lastReport = report;
        _reportStart = report.StartDate.Date;
        _reportEnd = report.EndDate.Date;
        _reportPresetDays = presetDays is 7 or 15 or 30 or 365 ? presetDays : null;
        Inventory.SetStatus($"Stock report · {BarStockSections.DisplayName(Inventory.SelectedSection)}");
        PaintCachedReport();
    }

    private void PaintCachedReport()
    {
        if (_lastReport is null)
        {
            return;
        }

        var section = Inventory.SelectedSection;
        var items = (_lastReport.Items ?? Array.Empty<BarStockWeeklyReportRowDto>())
            .Where(r => section == BarStockSections.All ||
                        string.Equals(BarStockSections.Normalize(r.Section), section, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Inventory.SetUsageReport(
            _lastReport.PeriodLabel,
            $"{BarStockQtyDisplay.FormatQty(items.Sum(i => i.EndingOnHand))} bottle{(Math.Abs(items.Sum(i => i.EndingOnHand)) == 1m ? "" : "s")}",
            $"{BarStockQtyDisplay.FormatQty(items.Sum(i => i.UsedTotal))} bottle{(Math.Abs(items.Sum(i => i.UsedTotal)) == 1m ? "" : "s")}",
            $"{BarStockQtyDisplay.FormatQty(items.Sum(i => i.WasteTotal))} bottle{(Math.Abs(items.Sum(i => i.WasteTotal)) == 1m ? "" : "s")}",
            _reportStart,
            _reportEnd,
            _reportPresetDays,
            items.Select(r => new BarStockWeeklyReportRowPresentation(
                r.Name,
                BarStockSections.DisplayName(r.Section),
                string.IsNullOrWhiteSpace(r.HaveDisplay) ? $"{r.EndingOnHand:0.###} {r.StockUnit}" : r.HaveDisplay,
                string.IsNullOrWhiteSpace(r.UsedDisplay) ? $"{r.UsedTotal:0.###} {r.StockUnit}" : r.UsedDisplay,
                string.IsNullOrWhiteSpace(r.WasteDisplay) ? $"{r.WasteTotal:0.###} {r.StockUnit}" : r.WasteDisplay,
                r.Sku)).ToList());
    }

    private async Task ExportUsageReportCsvAsync()
    {
        try
        {
            var path = await _barInventory.DownloadUsageReportCsvAsync(_reportStart, _reportEnd);
            if (string.IsNullOrWhiteSpace(path))
            {
                await DisplayAlert("Stock report CSV", "Could not download CSV from Mother.", "OK");
                return;
            }

            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                Title = "Stock report CSV",
                File = new ReadOnlyFile(path)
            });
            Inventory.SetStatus($"CSV saved · {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Stock report CSV", ex.Message, "OK");
        }
    }

    private async Task RunQuickAddAsync(BarStockBoardRowPresentation row)
    {
        if (string.IsNullOrWhiteSpace(row.StockId))
        {
            return;
        }

        Inventory.UnfocusSearch();
        SharedTouchKeyboard.SuppressAllBriefly(500);

        var fromSuggest = Inventory.IsSuggestMode;
        var stickyKey = GetStickyKey(BarStockMovementKind.Receive);
        var dialog = new BarStockQuickAddDialog();
        var result = await dialog.ShowAsync(this, row, async draft =>
        {
            var response = await _barInventory.ReceiveAsync(new BarStockReceiveRequestDto
            {
                StockId = draft.StockId ?? row.StockId,
                Qty = draft.QtyBottles,
                InputUnit = BarStockUnits.Bottle,
                Note = fromSuggest ? "Suggest delivery IN" : "Quick Stock IN",
                IdempotencyKey = stickyKey
            });

            if (!response.Success)
            {
                return (false, response.Message);
            }

            ClearStickyKey(BarStockMovementKind.Receive);
            return (true, null);
        }, deliveryFromSuggest: fromSuggest);

        if (!result.Confirmed)
        {
            return;
        }

        Inventory.SetStatus(fromSuggest
            ? $"Delivery +{result.QtyBottles:0.###} · {row.Name}"
            : $"Stock IN +{result.QtyBottles:0.###} · {row.Name}");
        await LoadBoardAsync(keepSuggestMode: fromSuggest);
    }

    private async Task RunMovementAsync(BarStockMovementKind kind)
    {
        if (_allBoard.Count == 0)
        {
            await LoadBoardAsync();
        }

        var sectionRows = FilterBySection(_allBoard, Inventory.SelectedSection);
        if (sectionRows.Count == 0)
        {
            await DisplayAlert(
                "Bar Inventory",
                "No tracked items in this section — Admin must enable Track on Add Item.",
                "OK");
            return;
        }

        var pickers = sectionRows.Select(r => new BarStockPickerItem(
            r.StockId, r.Name, r.StockUnit, r.OnHand, r.PackSize, r.ParLevel)).ToList();
        var stickyKey = GetStickyKey(kind);
        var dialog = new BarStockMovementDialog();
        await dialog.ShowAsync(this, kind, pickers, async draft =>
        {
            BarStockMovementResponseDto result = kind switch
            {
                BarStockMovementKind.Receive => await _barInventory.ReceiveAsync(new BarStockReceiveRequestDto
                {
                    StockId = draft.StockId ?? string.Empty,
                    Qty = draft.Qty,
                    InputUnit = draft.InputUnit,
                    Note = draft.NoteOrReason,
                    IdempotencyKey = draft.IdempotencyKey
                }),
                BarStockMovementKind.Waste => await _barInventory.WasteAsync(new BarStockWasteRequestDto
                {
                    StockId = draft.StockId ?? string.Empty,
                    Qty = draft.Qty,
                    Reason = draft.NoteOrReason ?? string.Empty,
                    IdempotencyKey = draft.IdempotencyKey
                }),
                _ => new BarStockMovementResponseDto { Success = false, Message = "Unknown action." }
            };

            if (!result.Success)
            {
                return (false, result.Message);
            }

            ClearStickyKey(kind);
            return (true, null);
        }, stickyKey);

        Inventory.SetStatus("Current Stock · updated");
        await LoadBoardAsync();
    }

    private string GetStickyKey(BarStockMovementKind kind)
    {
        var key = kind.ToString();
        if (!_stickyKeys.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            value = Guid.NewGuid().ToString("N");
            _stickyKeys[key] = value;
        }

        return value;
    }

    private void ClearStickyKey(BarStockMovementKind kind) => _stickyKeys.Remove(kind.ToString());

    private async Task ExportSuggestPdfAsync()
    {
        try
        {
            var path = await _barInventory.DownloadSuggestPdfAsync(Inventory.SelectedSection);
            if (string.IsNullOrWhiteSpace(path))
            {
                await DisplayAlert("Order", "Could not download PDF (Mother online?).", "OK");
                return;
            }

            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                Title = "Order list PDF",
                File = new ReadOnlyFile(path)
            });
            Inventory.SetStatus($"PDF saved · {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Order PDF", ex.Message, "OK");
        }
    }

    private bool HasBarInventoryAccess()
    {
        if (!ClientHostAccess.Features.Contains(PosFeatureKeys.BarInventory) &&
            !ClientHostAccess.Routes.Contains("inventory"))
        {
            return false;
        }

        return ClientHostAccess.CanOpenMenu("Bar Inventory") ||
               ClientHostAccess.RoutesForRole(ClientHostAccess.SessionRole).Contains("inventory");
    }

    private async Task RefreshAccessFromMotherAsync()
    {
        try
        {
            var session = await _cache.GetCurrentLoginSessionAsync();
            if (session is not null)
            {
                ClientHostAccess.ApplyFromSession(session);
                Sidebar.Role = session.Role ?? "BarManager";
            }
        }
        catch
        {
            // Keep last-good access when Mother refresh fails.
        }
    }

    private async Task OpenSidebarAsync()
    {
        SidebarLayer.IsVisible = true;
        Sidebar.TranslationX = -280;
        await Sidebar.TranslateTo(0, 0, 180, Easing.CubicOut);
    }

    private async void OnBackdropTapped(object? sender, EventArgs e)
    {
        await Sidebar.TranslateTo(-280, 0, 160, Easing.CubicIn);
        SidebarLayer.IsVisible = false;
    }
}

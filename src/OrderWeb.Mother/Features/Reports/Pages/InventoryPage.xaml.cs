using OrderWeb.Contracts.Dtos;
using OrderWeb.SharedUI.Controls;
using OrderWeb.SharedUI.Views;
using POS_in_NET.Models;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

public partial class InventoryPage : ContentPage
{
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;
    private readonly PermissionService _permissionService;
    private readonly BarStockService _barStockService;
    private readonly Dictionary<string, string> _stickyKeys = new(StringComparer.Ordinal);
    private bool _actionBusy;
    private IReadOnlyList<BarStockBoardRowDto> _allBoard = Array.Empty<BarStockBoardRowDto>();
    private BarStockWeeklyReportResponseDto? _lastReport;
    private DateTime _reportStart = DateTime.Today.AddDays(-6);
    private DateTime _reportEnd = DateTime.Today;
    private int? _reportPresetDays = 7;

    public InventoryPage()
    {
        InitializeComponent();
        TopBar.SetPageTitle("Bar Inventory");
        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        _permissionService = ServiceHelper.GetService<PermissionService>()
            ?? new PermissionService(new DatabaseService(), _authService);
        _barStockService = ServiceHelper.GetService<BarStockService>() ?? new BarStockService();

        Inventory.ActionRequested += OnInventoryActionRequested;
        Inventory.SectionChanged += async (_, _) =>
        {
            if (Inventory.IsReportMode)
            {
                PaintCachedReport();
            }
            else
            {
                ApplyFilteredBoard(suggestedOnly: Inventory.IsSuggestMode);
            }

            await Task.CompletedTask;
        };
        Inventory.SuggestPdfDownloadRequested += async (_, _) => await ExportSuggestPdfAsync();
        Inventory.ReportRangeRequested += async (_, range) =>
        {
            await ShowUsageReportAsync(range.StartDate, range.EndDate, range.PresetDays);
        };
        Inventory.ReportCsvDownloadRequested += async (_, _) => await ExportUsageReportCsvAsync();
        Inventory.ReportPdfDownloadRequested += async (_, _) => await ExportUsageReportPdfAsync();
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
        Inventory.EditRequested += async (_, row) =>
        {
            if (_actionBusy)
            {
                return;
            }

            try
            {
                _actionBusy = true;
                await RunEditStockAsync(row);
            }
            finally
            {
                _actionBusy = false;
            }
        };
        Inventory.SetBoard(Array.Empty<BarStockBoardRowPresentation>());
        Inventory.ShowAdminCreate = false;
        Inventory.ShowReport = false;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            if (!await SessionAccessGuard.RequireSignedInAsync(_authService))
            {
                return;
            }

            await _permissionService.EnsureInventoryRolePermissionsAsync();

            var role = _authService.CurrentUser?.Role;
            if (!_roleAccessService.CanAccessRoute(role, "inventory"))
            {
                await AppAlertService.ShowAlertAsync(
                    "Access Denied",
                    "Only Admin or Bar Manager can access Bar Inventory.");
                await NavigationCoordinator.Shared.NavigateShellAsync(
                    _roleAccessService.ResolveDashboardRoute(role));
                return;
            }

            Inventory.ShowAdminCreate = role is UserRole.Admin;
            Inventory.ShowReport = role is UserRole.Admin;
            var who = role is UserRole.BarManager ? "Bar Manager" : role?.ToString() ?? "Staff";
            Inventory.SetStatus($"{who} · Mother stock ledger · shared with Client tills");
            await LoadBoardAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Inventory page error: {ex.Message}");
            Inventory.SetStatus("Could not load stock board.");
            Inventory.SetBoard(Array.Empty<BarStockBoardRowPresentation>());
        }
    }

    private async Task LoadBoardAsync(bool keepSuggestMode = false)
    {
        _allBoard = await _barStockService.ListTrackedBoardAsync();
        Inventory.SetSectionChips(_allBoard.Select(r => r.Section));
        ApplyFilteredBoard(suggestedOnly: keepSuggestMode && Inventory.IsSuggestMode);
        // Soft Track Off note is Admin-facing only — hide on Bar Manager.
        if (_authService.CurrentUser?.Role == UserRole.Admin)
        {
            var advisory = await _barStockService.GetUntrackedDrinkAdvisoryAsync();
            Inventory.SetAdvisoryNote(advisory);
        }
        else
        {
            Inventory.SetAdvisoryNote(null);
        }
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
                await RunAddStockAsync();
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
                if (_authService.CurrentUser?.Role is not UserRole.Admin)
                {
                    await AppAlertService.ShowAlertAsync(
                        "Stock report",
                        "Stock report is available to Admin only. Bar Manager can Waste and Order.");
                    return;
                }

                await ShowUsageReportAsync(_reportStart, _reportEnd, _reportPresetDays);
                return;
            }

            if (action is BarInventoryActionKind.Waste)
            {
                await RunWasteAsync();
                return;
            }

            var kind = action switch
            {
                BarInventoryActionKind.Receive => BarStockMovementKind.Receive,
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

    private async Task RunAddStockAsync()
    {
        if (_authService.CurrentUser?.Role is not UserRole.Admin)
        {
            await AppAlertService.ShowAlertAsync("Bar Inventory", "Only Admin can add stock items.");
            return;
        }

        Inventory.UnfocusSearch();
        SharedTouchKeyboard.SuppressAllBriefly(500);

        var dialog = new BarStockCreateDialog();
        var result = await dialog.ShowAsync(this, Inventory.SelectedSection, async draft =>
        {
            var created = await _barStockService.CreateAdminStockAsync(new BarStockCreateRequestDto
            {
                Sku = draft.Sku ?? string.Empty,
                Name = draft.Name ?? string.Empty,
                Section = draft.Section ?? BarStockSections.Wine,
                MlPerBottle = draft.MlPerBottle,
                OpeningBottles = draft.StockInBottles,
                LowLevel = draft.LowLevel,
                MaxLevel = draft.MaxLevel,
                ParLevel = draft.MaxLevel
            });

            if (!created.Success)
            {
                return (false, created.Message);
            }

            try
            {
                var broadcast = ServiceHelper.GetService<ClientWebSocketBroadcastService>();
                if (broadcast is not null)
                {
                    await broadcast.PublishDataChangedAsync(
                        "barinventory.updated",
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
                }
            }
            catch
            {
                // Local board refresh still works.
            }

            return (true, null);
        });

        if (!result.Confirmed)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.Section))
        {
            Inventory.SetSelectedSection(result.Section);
        }

        await LoadBoardAsync();
        Inventory.SetStatus($"Added {result.Name}");
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
            var response = await _barStockService.ReceiveAsync(new BarStockReceiveRequestDto
            {
                StockId = draft.StockId ?? row.StockId,
                Qty = draft.QtyBottles,
                InputUnit = BarStockUnits.Bottle,
                Note = fromSuggest ? "Suggest delivery IN" : "Quick Stock IN",
                IdempotencyKey = stickyKey
            }, BuildActor());

            if (!response.Success)
            {
                return (false, response.Message);
            }

            ClearStickyKey(BarStockMovementKind.Receive);
            try
            {
                var broadcast = ServiceHelper.GetService<ClientWebSocketBroadcastService>();
                if (broadcast is not null)
                {
                    await broadcast.PublishDataChangedAsync(
                        "barinventory.updated",
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
                }
            }
            catch
            {
                // Local refresh still works.
            }

            return (true, null);
        }, deliveryFromSuggest: fromSuggest);

        if (!result.Confirmed)
        {
            return;
        }

        await LoadBoardAsync(keepSuggestMode: fromSuggest);
        Inventory.SetStatus(fromSuggest
            ? $"Delivery +{result.QtyBottles:0.###} · {row.Name}"
            : $"Stock IN +{result.QtyBottles:0.###} · {row.Name}");
    }

    private async Task RunEditStockAsync(BarStockBoardRowPresentation row)
    {
        if (_authService.CurrentUser?.Role is not UserRole.Admin)
        {
            await AppAlertService.ShowAlertAsync("Bar Inventory", "Only Admin can edit stock details.");
            return;
        }

        Inventory.UnfocusSearch();
        SharedTouchKeyboard.SuppressAllBriefly(500);

        var dialog = new BarStockEditDialog();
        var result = await dialog.ShowAsync(this, row, async draft =>
        {
            if (draft.Deleted)
            {
                var deleted = await _barStockService.DeleteAdminStockAsync(draft.StockId ?? row.StockId);
                if (!deleted.Success)
                {
                    return (false, deleted.Message);
                }
            }
            else
            {
                var updated = await _barStockService.UpdateAdminStockAsync(new BarStockUpdateRequestDto
                {
                    StockId = draft.StockId ?? row.StockId,
                    Sku = draft.Sku,
                    Name = draft.Name ?? string.Empty,
                    Section = draft.Section ?? BarStockSections.Other,
                    MlPerBottle = draft.MlPerBottle,
                    LowLevel = draft.LowLevel,
                    MaxLevel = draft.MaxLevel
                });

                if (!updated.Success)
                {
                    return (false, updated.Message);
                }
            }

            try
            {
                var broadcast = ServiceHelper.GetService<ClientWebSocketBroadcastService>();
                if (broadcast is not null)
                {
                    await broadcast.PublishDataChangedAsync(
                        "barinventory.updated",
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
                }
            }
            catch
            {
                // Local refresh still works.
            }

            return (true, null);
        });

        if (!result.Confirmed)
        {
            return;
        }

        if (result.Deleted)
        {
            await LoadBoardAsync();
            Inventory.SetStatus($"Deleted {row.Name}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.Section))
        {
            Inventory.SetSelectedSection(result.Section);
        }

        await LoadBoardAsync();
        Inventory.SetStatus($"Updated {result.Name}");
    }

    private async Task ShowUsageReportAsync(DateTime startDate, DateTime endDate, int? presetDays)
    {
        if (_authService.CurrentUser?.Role is not UserRole.Admin)
        {
            await AppAlertService.ShowAlertAsync(
                "Stock report",
                "Stock report is available to Admin only.");
            return;
        }

        if (endDate.Date < startDate.Date)
        {
            Inventory.SetStatus("End date cannot be before start date.");
            await AppAlertService.ShowAlertAsync("Stock report", "End date cannot be before start date.");
            return;
        }

        var report = presetDays is int days && days is 7 or 15 or 30 or 365
            ? await _barStockService.GetWeeklyReportAsync(days)
            : await _barStockService.GetUsageReportAsync(startDate.Date, endDate.Date);
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
        if (_authService.CurrentUser?.Role is not UserRole.Admin)
        {
            await AppAlertService.ShowAlertAsync(
                "Stock report CSV",
                "Stock report export is available to Admin only.");
            return;
        }

        try
        {
            var path = await _barStockService.ExportUsageReportCsvAsync(_reportStart, _reportEnd);
            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                Title = "Stock report CSV",
                File = new ReadOnlyFile(path)
            });
            Inventory.SetStatus($"CSV saved · {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Stock report CSV", ex.Message);
        }
    }

    private async Task ExportUsageReportPdfAsync()
    {
        if (_authService.CurrentUser?.Role is not UserRole.Admin)
        {
            await AppAlertService.ShowAlertAsync(
                "Stock report PDF",
                "Stock report export is available to Admin only.");
            return;
        }

        try
        {
            var path = await _barStockService.ExportUsageReportPdfAsync(_reportStart, _reportEnd);
            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                Title = "Stock report PDF",
                File = new ReadOnlyFile(path)
            });
            Inventory.SetStatus($"PDF saved · {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Stock report PDF", ex.Message);
        }
    }

    private async Task RunWasteAsync()
    {
        if (_allBoard.Count == 0)
        {
            await LoadBoardAsync();
        }

        if (_allBoard.Count == 0)
        {
            await AppAlertService.ShowAlertAsync(
                "Waste",
                Inventory.ShowAdminCreate
                    ? "No stock yet — tap Add stock to create one."
                    : "No tracked items — ask Admin to Add stock or enable Track on Add Item.");
            return;
        }

        Inventory.UnfocusSearch();
        SharedTouchKeyboard.SuppressAllBriefly(500);

        var pickers = _allBoard.Select(r => new BarStockPickerItem(
            r.StockId, r.Name, r.StockUnit, r.OnHand, r.PackSize, r.ParLevel)).ToList();
        var stickyKey = GetStickyKey(BarStockMovementKind.Waste);
        var dialog = new BarStockWasteDialog();
        var result = await dialog.ShowAsync(this, pickers, async draft =>
        {
            var response = await _barStockService.WasteAsync(new BarStockWasteRequestDto
            {
                StockId = draft.StockId ?? string.Empty,
                Qty = draft.QtyInStockUnit,
                Reason = draft.Reason ?? string.Empty,
                IdempotencyKey = draft.IdempotencyKey ?? stickyKey
            }, BuildActor());

            if (!response.Success)
            {
                return (false, response.Message);
            }

            ClearStickyKey(BarStockMovementKind.Waste);
            try
            {
                var broadcast = ServiceHelper.GetService<ClientWebSocketBroadcastService>();
                if (broadcast is not null)
                {
                    await broadcast.PublishDataChangedAsync(
                        "barinventory.updated",
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
                }
            }
            catch
            {
                // Local refresh still works.
            }

            return (true, null);
        }, stickyKey);

        if (!result.Confirmed)
        {
            return;
        }

        await LoadBoardAsync();
        Inventory.SetStatus($"Waste −{result.QtyInStockUnit:0.###} · {result.Reason}");
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
            await AppAlertService.ShowAlertAsync(
                "Bar Inventory",
                Inventory.ShowAdminCreate
                    ? "No stock in this section — tap Add stock to create one."
                    : "No tracked items in this section — ask Admin to Add stock or enable Track on Add Item.");
            return;
        }

        var pickers = sectionRows.Select(r => new BarStockPickerItem(
            r.StockId, r.Name, r.StockUnit, r.OnHand, r.PackSize, r.ParLevel)).ToList();

        var stickyKey = GetStickyKey(kind);
        var dialog = new BarStockMovementDialog();
        await dialog.ShowAsync(this, kind, pickers, async draft =>
        {
            var actor = BuildActor();
            BarStockMovementResponseDto result = kind switch
            {
                BarStockMovementKind.Receive => await _barStockService.ReceiveAsync(new BarStockReceiveRequestDto
                {
                    StockId = draft.StockId ?? string.Empty,
                    Qty = draft.Qty,
                    InputUnit = draft.InputUnit,
                    Note = draft.NoteOrReason,
                    IdempotencyKey = draft.IdempotencyKey
                }, actor),
                BarStockMovementKind.Waste => await _barStockService.WasteAsync(new BarStockWasteRequestDto
                {
                    StockId = draft.StockId ?? string.Empty,
                    Qty = draft.Qty,
                    Reason = draft.NoteOrReason ?? string.Empty,
                    IdempotencyKey = draft.IdempotencyKey
                }, actor),
                _ => new BarStockMovementResponseDto { Success = false, Message = "Unknown action." }
            };

            if (!result.Success)
            {
                return (false, result.Message);
            }

            ClearStickyKey(kind);
            try
            {
                var broadcast = ServiceHelper.GetService<ClientWebSocketBroadcastService>();
                if (broadcast is not null)
                {
                    await broadcast.PublishDataChangedAsync(
                        "barinventory.updated",
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString());
                }
            }
            catch
            {
                // Board refresh still works locally.
            }

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
            var path = await _barStockService.ExportSuggestedOrderPdfAsync(Inventory.SelectedSection);
            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                Title = "Order list PDF",
                File = new ReadOnlyFile(path)
            });
            Inventory.SetStatus($"PDF saved · {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Order PDF", ex.Message);
        }
    }

    private BarStockMovementActorDto BuildActor()
    {
        var user = _authService.CurrentUser;
        var config = TerminalConfigurationService.GetConfiguration();
        return new BarStockMovementActorDto
        {
            StaffUserId = user?.Id.ToString(),
            StaffDisplayName = user?.Username,
            TerminalId = config.TerminalId,
            TerminalLabel = config.TerminalName,
            Source = "mother"
        };
    }
}

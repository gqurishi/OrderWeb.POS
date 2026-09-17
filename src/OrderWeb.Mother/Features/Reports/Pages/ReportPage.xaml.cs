using System.Collections.ObjectModel;
using System.Globalization;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;
using CommunityToolkit.Maui.Storage;
using POS_in_NET.Models;
using POS_in_NET.Services;
using POS_in_NET.Views;
using POS_in_NET.Helpers;

namespace POS_in_NET.Pages;

public partial class ReportPage : ContentPage
{
    private readonly AuthenticationService _authService;
    private readonly RoleAccessService _roleAccessService;
    private readonly PermissionService _permissionService;
    private readonly DailyReportService _reportService;
    private readonly ReportGenerationService _reportGenerationService;
    private readonly ReportHistoryService _reportHistoryService;
    private readonly BusinessSettingsService _businessSettingsService;
    private readonly CashDrawerService _cashDrawerService;
    private readonly DiscountAuditService _discountAuditService;
    private readonly TillExpenseService _tillExpenseService;
    private readonly ZReportService _zReportService;
    private readonly ZReportPrintService _zReportPrintService;
    private readonly OrderWebDailyReportSyncService _orderWebDailyReportSyncService;
    private readonly TimeClockService _timeClockService;

    private bool _hasLoaded;
    private bool _isLoading;
    private bool _isSubscribedToRefreshEvents;
    private ReportDatePreset _selectedPreset = ReportDatePreset.Today;
    private DateTime _startDate = DateTime.Today;
    private DateTime _endDate = DateTime.Today;
    private string _searchText = string.Empty;
    private string _selectedSourceFilter = ReportSourceFilter.All.ToString();
    private string _selectedOrderTypeFilter = ReportOrderTypeFilter.All.ToString();
    private bool _isCustomRangeVisible;
    private string _lastUpdatedText = "Ready to load live report data";
    private string _summaryOrdersText = "0";
    private string _summaryGrossText = "£0.00";
    private string _summaryNetText = "£0.00";
    private string _summaryVatText = "£0.00";
    private string _summaryDeliveryText = "£0.00";
    private string _summaryServiceChargeText = "£0.00";
    private string _summaryRemovedChargeText = "£0.00";
    private string _summaryCashTipsText = "£0.00";
    private string _summaryCardTipsText = "£0.00";
    private string _summaryRefundsText = "£0.00";
    private string _summaryCollectedText = "£0.00";
    private string _summaryCashPaidText = "£0.00";
    private string _summaryCardPaidText = "£0.00";
    private string _summaryGiftCardPaidText = "£0.00";
    private string _summaryAverageText = "£0.00";
    private string _summaryTotalTipsText = "£0.00";
    private bool _isCustomRangeDirty;
    private ReportViewMode _reportViewMode = ReportViewMode.Orders;
    private int _topSellRangeDays = 30;
    private int _vatRangeDays = 30;
    private TopSellSection _selectedTopSellSection = TopSellSection.Food;
    private bool _isTopSellCustomRangeVisible;
    private bool _isTopSellCustomRangeDirty;
    private bool _isVatCustomRangeVisible;
    private bool _isVatCustomRangeDirty;
    private bool _isCalendarPopupVisible;
    private string _vatHeroText = "—";
    private string _vatGrossText = "—";
    private string _vatNetText = "—";
    private string _vatPeriodLabel = "Select a VAT period";
    private string _vatStatusText = "Choose 7, 15, 30, 180, or 365 days — or Custom dates. Totals load in the next phase.";
    private string _vatErrorText = string.Empty;
    private bool _hasVatError;
    private string _vatZeroTaxWarningText = string.Empty;
    private bool _hasVatZeroTaxWarning;
    private string _vatRateBandsInsightText = string.Empty;
    private VatPeriodSnapshot? _lastVatPeriod;
    private string _operationalSendLatencyText = "No samples";
    private string _operationalPaymentCompletionText = "No samples";
    private string _operationalVoidAuditText = "No voids";
    private string _operationalDraftAbandonmentText = "0 drafts";
    private string _cashDrawerOpensText = "0 opens";
    private string _cashDrawerFailuresText = "0 failed";
    private string _cashDrawerLastOpenText = "No drawer opens in this range";
    private string _tillExpensePendingText = "0 pending";
    private string _tillExpenseNetOutText = "£0.00 out";
    private string _tillExpenseShoppingText = "£0.00";
    private string _tillExpenseDeliveryText = "£0.00";
    private string _tillExpenseRefundText = "£0.00";
    private string _tillExpenseOtherText = "£0.00";
    private string _tillExpenseCashReturnText = "£0.00";
    private string _tillExpenseCashCountText = "0 counts";
    private string _discountEventsText = "0 events";
    private string _discountTotalText = "£0.00";
    private string _discountLastEventText = "No discounts in this range";
    private string _voidCancelledTotalText = "0 orders";
    private string _voidCancelledAmountText = "£0.00";
    private string _voidCancelledVoidedText = "0 voided";
    private string _voidCancelledCancelledText = "0 cancelled";
    private string _voidCancelledLastEventText = "No voided or cancelled orders in this range";
    private string _calendarPopupTitle = "Select Date";
    private string _calendarMonthLabel = DateTime.Today.ToString("MMMM yyyy");
    private DateTime _calendarDisplayedMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private DateTime _calendarDraftDate = DateTime.Today;
    private CalendarTarget _calendarTarget = CalendarTarget.Start;
    private bool _isComparisonPanelVisible;
    private string _comparisonPriorText = "Select a historical snapshot to compare the prior period.";
    private string _comparisonYearAgoText = "Year-over-year comparison will appear here.";
    private string _trendInsightText = "Trend insight appears when enough data is available.";
    private string _orderWebUploadBannerText = string.Empty;
    private bool _isOrderWebUploadBannerVisible;
    private Color _trendInsightColor = Color.FromArgb("#334155");
    private Color _summaryOrdersColor = Color.FromArgb("#0F172A");
    private Color _summaryGrossColor = Color.FromArgb("#0F172A");
    private Color _summaryNetColor = Color.FromArgb("#0F172A");
    private Color _summaryVatColor = Color.FromArgb("#0F172A");
    private Color _summaryDeliveryColor = Color.FromArgb("#0F172A");
    private Color _summaryAverageColor = Color.FromArgb("#0F172A");
    private Color _operationalSendLatencyColor = Color.FromArgb("#0F172A");
    private Color _operationalPaymentCompletionColor = Color.FromArgb("#0F172A");
    private Color _operationalDraftAbandonmentColor = Color.FromArgb("#0F172A");
    private Color _operationalVoidAuditColor = Color.FromArgb("#0F172A");
    private Color _cashDrawerOpensColor = Color.FromArgb("#0F172A");
    private Color _cashDrawerFailuresColor = Color.FromArgb("#0F172A");
    private Color _discountEventsColor = Color.FromArgb("#0F172A");
    private Color _discountTotalColor = Color.FromArgb("#0F172A");
    private string _staffHoursTotalText = "0h 0m";
    private string _staffHoursStaffCountText = "0 staff";
    private string _staffHoursOpenText = "0 open shifts";
    private bool _isOrdersSearchKeyboardOpen;

    private enum ReportViewMode
    {
        Orders,
        TopSellItems,
        CashDrawer,
        DiscountAudit,
        StaffHours,
        VoidCancelled,
        Vat,
        HistoricalReports
    }

    private enum CalendarTarget
    {
        Start,
        End
    }

    private void OnReportPageSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        var profile = TabletLayoutHelper.GetProfile(Width, Height);
        ReportContentStack.Padding = new Thickness(0, 0, 0, profile.SafeBottom);
        OrdersCollection.HeightRequest = Math.Max(300, Math.Min(700, Height - 240));

        var cardMargin = new Thickness(
            profile.PagePadding,
            Math.Max(8, profile.PagePadding - 6),
            profile.PagePadding,
            0);
        foreach (var card in ReportContentStack.Children.OfType<Border>())
            card.Margin = cardMargin;
    }

    public ObservableCollection<ReportOrderRow> Orders { get; } = new();
    public ObservableCollection<ReportTopItemRow> TopItems { get; } = new();
    public ObservableCollection<OperationalVoidAuditRow> VoidAudits { get; } = new();
    public ObservableCollection<CashDrawerReportRow> CashDrawerAudits { get; } = new();
    public ObservableCollection<TillExpenseReportRow> TillExpenses { get; } = new();
    public ObservableCollection<DiscountAuditReportRow> DiscountAudits { get; } = new();
    public ObservableCollection<ReportVoidCancelledRow> VoidCancelledOrders { get; } = new();
    public ObservableCollection<LabourReportRow> StaffHoursRows { get; } = new();
    public ObservableCollection<ReportHistorySummary> HistoricalReports { get; } = new();
    public ObservableCollection<ReportDailyTrendRow> DailyTrend { get; } = new();
    public ObservableCollection<ServiceChargeRemovalAuditRow> ServiceChargeRemovalAudits { get; } = new();
    public ObservableCollection<VatRateBandRow> VatRateBands { get; } = new();
    public ObservableCollection<string> SourceFilters { get; } = new() { "All", "Local", "Web" };
    public ObservableCollection<string> OrderTypeFilters { get; } = new() { "All", "Pickup", "Delivery", "Table" };

    public bool IsTopItemsEmpty => TopItems.Count == 0;
    public bool HasVoidAudits => VoidAudits.Count > 0;
    public bool HasCashDrawerAudits => CashDrawerAudits.Count > 0;
    public bool HasStaffHoursRows => StaffHoursRows.Count > 0;
    public bool HasTillExpenses => TillExpenses.Count > 0;
    public bool HasDiscountAudits => DiscountAudits.Count > 0;
    public bool HasVoidCancelledOrders => VoidCancelledOrders.Count > 0;
    public bool HasHistoricalReports => HistoricalReports.Count > 0;
    public bool HasDailyTrend => DailyTrend.Count > 1;
    public bool HasServiceChargeRemovalAudits => ServiceChargeRemovalAudits.Count > 0;

    public DateTime StartDate
    {
        get => _startDate;
        set
        {
            if (_startDate != value)
            {
                _startDate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StartDateDisplay));
            }
        }
    }

    public string StartDateDisplay => _startDate.ToString("MMM dd, yyyy");

    public DateTime EndDate
    {
        get => _endDate;
        set
        {
            if (_endDate != value)
            {
                _endDate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EndDateDisplay));
            }
        }
    }

    public string EndDateDisplay => _endDate.ToString("MMM dd, yyyy");

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                OnPropertyChanged();
            }
        }
    }

    public string SelectedSourceFilter
    {
        get => _selectedSourceFilter;
        set
        {
            if (_selectedSourceFilter != value)
            {
                _selectedSourceFilter = value;
                OnPropertyChanged();
            }
        }
    }

    public string SelectedOrderTypeFilter
    {
        get => _selectedOrderTypeFilter;
        set
        {
            if (_selectedOrderTypeFilter != value)
            {
                _selectedOrderTypeFilter = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsCustomRangeVisible
    {
        get => _isCustomRangeVisible;
        set
        {
            if (_isCustomRangeVisible != value)
            {
                _isCustomRangeVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSubmitCustomRange));
            }
        }
    }

    public bool IsCustomRangeDirty
    {
        get => _isCustomRangeDirty;
        set
        {
            if (_isCustomRangeDirty != value)
            {
                _isCustomRangeDirty = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSubmitCustomRange));
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading != value)
            {
                _isLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsNotLoading));
                OnPropertyChanged(nameof(CanSubmitCustomRange));
                OnPropertyChanged(nameof(CanSubmitTopSellCustomRange));
                OnPropertyChanged(nameof(CanSubmitVatCustomRange));
                OnPropertyChanged(nameof(CanExportVatSummary));
            }
        }
    }

    public bool IsNotLoading => !IsLoading;
    public bool CanSubmitCustomRange => IsCustomRangeVisible && IsCustomRangeDirty && !IsLoading;
    public bool CanUseFullReportTools => TerminalRoleService.CanViewFullReports;
    public bool CanUseAuditReports => CanUseFullReportTools;
    public bool CanExportReports => CanUseFullReportTools;
    public bool CanUploadOrderWebReport =>
        _roleAccessService?.IsAdmin(_authService?.CurrentUser?.Role) == true && CanUseFullReportTools;

    /// <summary>Admin-only permanent delete of local voided orders from Void / Cancelled audit.</summary>
    public bool CanPermanentlyDeleteVoidOrders => CanUploadOrderWebReport;

    public bool IsOrderWebUploadBannerVisible
    {
        get => _isOrderWebUploadBannerVisible;
        private set
        {
            if (_isOrderWebUploadBannerVisible != value)
            {
                _isOrderWebUploadBannerVisible = value;
                OnPropertyChanged();
            }
        }
    }

    public string OrderWebUploadBannerText
    {
        get => _orderWebUploadBannerText;
        private set
        {
            if (_orderWebUploadBannerText != value)
            {
                _orderWebUploadBannerText = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsLimitedReportTerminal => TerminalConfigurationService.IsChildTerminal;
    public string ReportTerminalModeText => IsLimitedReportTerminal
        ? "Child terminal limited report mode: live summaries are read from the shared mother database. End-of-day, historical comparison, exports, order drill-down, cash drawer, and discount audit reports run on the mother terminal."
        : "Mother terminal full report mode: live reports, end-of-day snapshots, history, exports, cash drawer, and audit reports are available.";

    public bool IsOrdersReportVisible => _reportViewMode == ReportViewMode.Orders;
    public bool IsHistoricalReportsVisible => _reportViewMode == ReportViewMode.HistoricalReports && CanUseFullReportTools;

    public bool IsTopSellReportVisible => _reportViewMode == ReportViewMode.TopSellItems;

    public bool IsVatReportVisible => _reportViewMode == ReportViewMode.Vat && CanUseFullReportTools;

    public bool IsCashDrawerReportVisible => _reportViewMode == ReportViewMode.CashDrawer && CanUseAuditReports;

    public bool IsDiscountAuditReportVisible => _reportViewMode == ReportViewMode.DiscountAudit && CanUseAuditReports;

    public bool IsStaffHoursReportVisible => _reportViewMode == ReportViewMode.StaffHours && CanUseAuditReports;

    public bool IsVoidCancelledReportVisible => _reportViewMode == ReportViewMode.VoidCancelled && CanUseAuditReports;

    public string VoidCancelledTotalText
    {
        get => _voidCancelledTotalText;
        set
        {
            if (_voidCancelledTotalText != value)
            {
                _voidCancelledTotalText = value;
                OnPropertyChanged();
            }
        }
    }

    public string VoidCancelledAmountText
    {
        get => _voidCancelledAmountText;
        set
        {
            if (_voidCancelledAmountText != value)
            {
                _voidCancelledAmountText = value;
                OnPropertyChanged();
            }
        }
    }

    public string VoidCancelledVoidedText
    {
        get => _voidCancelledVoidedText;
        set
        {
            if (_voidCancelledVoidedText != value)
            {
                _voidCancelledVoidedText = value;
                OnPropertyChanged();
            }
        }
    }

    public string VoidCancelledCancelledText
    {
        get => _voidCancelledCancelledText;
        set
        {
            if (_voidCancelledCancelledText != value)
            {
                _voidCancelledCancelledText = value;
                OnPropertyChanged();
            }
        }
    }

    public string VoidCancelledLastEventText
    {
        get => _voidCancelledLastEventText;
        set
        {
            if (_voidCancelledLastEventText != value)
            {
                _voidCancelledLastEventText = value;
                OnPropertyChanged();
            }
        }
    }

    public string StaffHoursTotalText
    {
        get => _staffHoursTotalText;
        set
        {
            if (_staffHoursTotalText != value)
            {
                _staffHoursTotalText = value;
                OnPropertyChanged();
            }
        }
    }

    public string StaffHoursStaffCountText
    {
        get => _staffHoursStaffCountText;
        set
        {
            if (_staffHoursStaffCountText != value)
            {
                _staffHoursStaffCountText = value;
                OnPropertyChanged();
            }
        }
    }

    public string StaffHoursOpenText
    {
        get => _staffHoursOpenText;
        set
        {
            if (_staffHoursOpenText != value)
            {
                _staffHoursOpenText = value;
                OnPropertyChanged();
            }
        }
    }

    public int TopSellRangeDays
    {
        get => _topSellRangeDays;
        set
        {
            if (_topSellRangeDays != value)
            {
                _topSellRangeDays = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TopSellRangeLabel));
                OnPropertyChanged(nameof(TopSellInsightText));
            }
        }
    }

    public string TopSellRangeLabel => $"Last {TopSellRangeDays} Days";
    public string TopSellInsightText => IsTopSellCustomRangeVisible
        ? $"Showing {_selectedTopSellSection} menu performance from {StartDate:dd MMM yyyy} to {EndDate:dd MMM yyyy}."
        : $"Showing {_selectedTopSellSection} menu performance for the last {TopSellRangeDays} days.";

    public bool IsTopSellFoodSelected => _selectedTopSellSection == TopSellSection.Food;

    public bool IsTopSellDrinkSelected => _selectedTopSellSection == TopSellSection.Drink;

    public bool IsTopSellCustomRangeVisible
    {
        get => _isTopSellCustomRangeVisible;
        set
        {
            if (_isTopSellCustomRangeVisible != value)
            {
                _isTopSellCustomRangeVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TopSellInsightText));
                OnPropertyChanged(nameof(CanSubmitTopSellCustomRange));
            }
        }
    }

    public bool IsTopSellCustomRangeDirty
    {
        get => _isTopSellCustomRangeDirty;
        set
        {
            if (_isTopSellCustomRangeDirty != value)
            {
                _isTopSellCustomRangeDirty = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSubmitTopSellCustomRange));
            }
        }
    }

    public bool CanSubmitTopSellCustomRange => IsTopSellCustomRangeVisible && IsTopSellCustomRangeDirty && !IsLoading;

    public int VatRangeDays
    {
        get => _vatRangeDays;
        set
        {
            if (_vatRangeDays != value)
            {
                _vatRangeDays = value;
                OnPropertyChanged();
                RefreshVatPeriodChrome();
            }
        }
    }

    public bool IsVatCustomRangeVisible
    {
        get => _isVatCustomRangeVisible;
        set
        {
            if (_isVatCustomRangeVisible != value)
            {
                _isVatCustomRangeVisible = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSubmitVatCustomRange));
                RefreshVatPeriodChrome();
            }
        }
    }

    public bool IsVatCustomRangeDirty
    {
        get => _isVatCustomRangeDirty;
        set
        {
            if (_isVatCustomRangeDirty != value)
            {
                _isVatCustomRangeDirty = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanSubmitVatCustomRange));
                RefreshVatPeriodChrome();
            }
        }
    }

    public bool CanSubmitVatCustomRange => IsVatCustomRangeVisible && IsVatCustomRangeDirty && !IsLoading;

    public string VatHeroText
    {
        get => _vatHeroText;
        set
        {
            if (_vatHeroText != value)
            {
                _vatHeroText = value;
                OnPropertyChanged();
            }
        }
    }

    public string VatGrossText
    {
        get => _vatGrossText;
        set
        {
            if (_vatGrossText != value)
            {
                _vatGrossText = value;
                OnPropertyChanged();
            }
        }
    }

    public string VatNetText
    {
        get => _vatNetText;
        set
        {
            if (_vatNetText != value)
            {
                _vatNetText = value;
                OnPropertyChanged();
            }
        }
    }

    public string VatPeriodLabel
    {
        get => _vatPeriodLabel;
        set
        {
            if (_vatPeriodLabel != value)
            {
                _vatPeriodLabel = value;
                OnPropertyChanged();
            }
        }
    }

    public string VatStatusText
    {
        get => _vatStatusText;
        set
        {
            if (_vatStatusText != value)
            {
                _vatStatusText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsVatStatusVisible));
            }
        }
    }

    public string VatErrorText
    {
        get => _vatErrorText;
        set
        {
            if (_vatErrorText != value)
            {
                _vatErrorText = value;
                OnPropertyChanged();
            }
        }
    }

    public bool HasVatError
    {
        get => _hasVatError;
        set
        {
            if (_hasVatError != value)
            {
                _hasVatError = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsVatStatusVisible));
            }
        }
    }

    public bool HasVatZeroTaxWarning
    {
        get => _hasVatZeroTaxWarning;
        set
        {
            if (_hasVatZeroTaxWarning != value)
            {
                _hasVatZeroTaxWarning = value;
                OnPropertyChanged();
            }
        }
    }

    public string VatZeroTaxWarningText
    {
        get => _vatZeroTaxWarningText;
        set
        {
            if (_vatZeroTaxWarningText != value)
            {
                _vatZeroTaxWarningText = value;
                OnPropertyChanged();
            }
        }
    }

    public string VatRateBandsInsightText
    {
        get => _vatRateBandsInsightText;
        set
        {
            if (_vatRateBandsInsightText != value)
            {
                _vatRateBandsInsightText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasVatRateBandsInsight));
            }
        }
    }

    public bool HasVatRateBandsInsight => !string.IsNullOrWhiteSpace(VatRateBandsInsightText);

    public bool HasVatRateBands => VatRateBands.Count > 0;

    public bool CanExportVatSummary =>
        CanExportReports
        && _reportViewMode == ReportViewMode.Vat
        && _lastVatPeriod != null
        && !IsLoading
        && !(IsVatCustomRangeVisible && IsVatCustomRangeDirty)
        && !HasVatError;

    public bool IsVatStatusVisible => !HasVatError && !string.IsNullOrWhiteSpace(VatStatusText);

    public string OperationalSendLatencyText
    {
        get => _operationalSendLatencyText;
        set
        {
            if (_operationalSendLatencyText != value)
            {
                _operationalSendLatencyText = value;
                OnPropertyChanged();
            }
        }
    }

    public string OperationalPaymentCompletionText
    {
        get => _operationalPaymentCompletionText;
        set
        {
            if (_operationalPaymentCompletionText != value)
            {
                _operationalPaymentCompletionText = value;
                OnPropertyChanged();
            }
        }
    }

    public string OperationalVoidAuditText
    {
        get => _operationalVoidAuditText;
        set
        {
            if (_operationalVoidAuditText != value)
            {
                _operationalVoidAuditText = value;
                OnPropertyChanged();
            }
        }
    }

    public string OperationalDraftAbandonmentText
    {
        get => _operationalDraftAbandonmentText;
        set
        {
            if (_operationalDraftAbandonmentText != value)
            {
                _operationalDraftAbandonmentText = value;
                OnPropertyChanged();
            }
        }
    }

    public string CashDrawerOpensText
    {
        get => _cashDrawerOpensText;
        set
        {
            if (_cashDrawerOpensText != value)
            {
                _cashDrawerOpensText = value;
                OnPropertyChanged();
            }
        }
    }

    public string CashDrawerFailuresText
    {
        get => _cashDrawerFailuresText;
        set
        {
            if (_cashDrawerFailuresText != value)
            {
                _cashDrawerFailuresText = value;
                OnPropertyChanged();
            }
        }
    }

    public string CashDrawerLastOpenText
    {
        get => _cashDrawerLastOpenText;
        set
        {
            if (_cashDrawerLastOpenText != value)
            {
                _cashDrawerLastOpenText = value;
                OnPropertyChanged();
            }
        }
    }

    public string TillExpensePendingText
    {
        get => _tillExpensePendingText;
        set
        {
            if (_tillExpensePendingText != value)
            {
                _tillExpensePendingText = value;
                OnPropertyChanged();
            }
        }
    }

    public string TillExpenseNetOutText
    {
        get => _tillExpenseNetOutText;
        set
        {
            if (_tillExpenseNetOutText != value)
            {
                _tillExpenseNetOutText = value;
                OnPropertyChanged();
            }
        }
    }

    public string TillExpenseShoppingText
    {
        get => _tillExpenseShoppingText;
        set
        {
            if (_tillExpenseShoppingText != value)
            {
                _tillExpenseShoppingText = value;
                OnPropertyChanged();
            }
        }
    }

    public string TillExpenseDeliveryText
    {
        get => _tillExpenseDeliveryText;
        set
        {
            if (_tillExpenseDeliveryText != value)
            {
                _tillExpenseDeliveryText = value;
                OnPropertyChanged();
            }
        }
    }

    public string TillExpenseRefundText
    {
        get => _tillExpenseRefundText;
        set
        {
            if (_tillExpenseRefundText != value)
            {
                _tillExpenseRefundText = value;
                OnPropertyChanged();
            }
        }
    }

    public string TillExpenseOtherText
    {
        get => _tillExpenseOtherText;
        set
        {
            if (_tillExpenseOtherText != value)
            {
                _tillExpenseOtherText = value;
                OnPropertyChanged();
            }
        }
    }

    public string TillExpenseCashReturnText
    {
        get => _tillExpenseCashReturnText;
        set
        {
            if (_tillExpenseCashReturnText != value)
            {
                _tillExpenseCashReturnText = value;
                OnPropertyChanged();
            }
        }
    }

    public string TillExpenseCashCountText
    {
        get => _tillExpenseCashCountText;
        set
        {
            if (_tillExpenseCashCountText != value)
            {
                _tillExpenseCashCountText = value;
                OnPropertyChanged();
            }
        }
    }

    public string DiscountEventsText
    {
        get => _discountEventsText;
        set
        {
            if (_discountEventsText != value)
            {
                _discountEventsText = value;
                OnPropertyChanged();
            }
        }
    }

    public string DiscountTotalText
    {
        get => _discountTotalText;
        set
        {
            if (_discountTotalText != value)
            {
                _discountTotalText = value;
                OnPropertyChanged();
            }
        }
    }

    public string DiscountLastEventText
    {
        get => _discountLastEventText;
        set
        {
            if (_discountLastEventText != value)
            {
                _discountLastEventText = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsCalendarPopupVisible
    {
        get => _isCalendarPopupVisible;
        set
        {
            if (_isCalendarPopupVisible != value)
            {
                _isCalendarPopupVisible = value;
                OnPropertyChanged();
            }
        }
    }

    public string CalendarPopupTitle
    {
        get => _calendarPopupTitle;
        set
        {
            if (_calendarPopupTitle != value)
            {
                _calendarPopupTitle = value;
                OnPropertyChanged();
            }
        }
    }

    public string CalendarMonthLabel
    {
        get => _calendarMonthLabel;
        set
        {
            if (_calendarMonthLabel != value)
            {
                _calendarMonthLabel = value;
                OnPropertyChanged();
            }
        }
    }

    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        set
        {
            if (_lastUpdatedText != value)
            {
                _lastUpdatedText = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsComparisonPanelVisible
    {
        get => _isComparisonPanelVisible;
        set
        {
            if (_isComparisonPanelVisible != value)
            {
                _isComparisonPanelVisible = value;
                OnPropertyChanged();
            }
        }
    }

    public string ComparisonPanelText
    {
        get => $"{ComparisonPriorText}\n\n{ComparisonYearAgoText}";
    }

    public string ComparisonPriorText
    {
        get => _comparisonPriorText;
        set
        {
            if (_comparisonPriorText != value)
            {
                _comparisonPriorText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ComparisonPanelText));
            }
        }
    }

    public string ComparisonYearAgoText
    {
        get => _comparisonYearAgoText;
        set
        {
            if (_comparisonYearAgoText != value)
            {
                _comparisonYearAgoText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ComparisonPanelText));
            }
        }
    }

    public string SummaryOrdersText
    {
        get => _summaryOrdersText;
        set
        {
            if (_summaryOrdersText != value)
            {
                _summaryOrdersText = value;
                OnPropertyChanged();
            }
        }
    }

    public string SummaryGrossText
    {
        get => _summaryGrossText;
        set
        {
            if (_summaryGrossText != value)
            {
                _summaryGrossText = value;
                OnPropertyChanged();
            }
        }
    }

    public string SummaryNetText
    {
        get => _summaryNetText;
        set
        {
            if (_summaryNetText != value)
            {
                _summaryNetText = value;
                OnPropertyChanged();
            }
        }
    }

    public string SummaryVatText
    {
        get => _summaryVatText;
        set
        {
            if (_summaryVatText != value)
            {
                _summaryVatText = value;
                OnPropertyChanged();
            }
        }
    }

    public string SummaryDeliveryText
    {
        get => _summaryDeliveryText;
        set
        {
            if (_summaryDeliveryText != value)
            {
                _summaryDeliveryText = value;
                OnPropertyChanged();
            }
        }
    }

    public string SummaryAverageText
    {
        get => _summaryAverageText;
        set
        {
            if (_summaryAverageText != value)
            {
                _summaryAverageText = value;
                OnPropertyChanged();
            }
        }
    }

    public string SummaryServiceChargeText => _summaryServiceChargeText;
    public string SummaryRemovedChargeText => _summaryRemovedChargeText;
    public string SummaryCashTipsText => _summaryCashTipsText;
    public string SummaryCardTipsText => _summaryCardTipsText;
    public string SummaryTotalTipsText => _summaryTotalTipsText;
    public string SummaryRefundsText => _summaryRefundsText;
    public string SummaryCollectedText => _summaryCollectedText;
    public string SummaryCashPaidText => _summaryCashPaidText;
    public string SummaryCardPaidText => _summaryCardPaidText;
    public string SummaryGiftCardPaidText => _summaryGiftCardPaidText;

    public Color SummaryOrdersColor
    {
        get => _summaryOrdersColor;
        set
        {
            if (_summaryOrdersColor != value)
            {
                _summaryOrdersColor = value;
                OnPropertyChanged();
            }
        }
    }

    public Color SummaryGrossColor
    {
        get => _summaryGrossColor;
        set
        {
            if (_summaryGrossColor != value)
            {
                _summaryGrossColor = value;
                OnPropertyChanged();
            }
        }
    }

    public Color SummaryNetColor
    {
        get => _summaryNetColor;
        set
        {
            if (_summaryNetColor != value)
            {
                _summaryNetColor = value;
                OnPropertyChanged();
            }
        }
    }

    public Color SummaryVatColor
    {
        get => _summaryVatColor;
        set
        {
            if (_summaryVatColor != value)
            {
                _summaryVatColor = value;
                OnPropertyChanged();
            }
        }
    }

    public Color SummaryDeliveryColor
    {
        get => _summaryDeliveryColor;
        set
        {
            if (_summaryDeliveryColor != value)
            {
                _summaryDeliveryColor = value;
                OnPropertyChanged();
            }
        }
    }

    public Color SummaryAverageColor
    {
        get => _summaryAverageColor;
        set
        {
            if (_summaryAverageColor != value)
            {
                _summaryAverageColor = value;
                OnPropertyChanged();
            }
        }
    }

    public Color OperationalSendLatencyColor
    {
        get => _operationalSendLatencyColor;
        set
        {
            if (_operationalSendLatencyColor != value)
            {
                _operationalSendLatencyColor = value;
                OnPropertyChanged();
            }
        }
    }

    public Color OperationalPaymentCompletionColor
    {
        get => _operationalPaymentCompletionColor;
        set
        {
            if (_operationalPaymentCompletionColor != value)
            {
                _operationalPaymentCompletionColor = value;
                OnPropertyChanged();
            }
        }
    }

    public Color OperationalDraftAbandonmentColor
    {
        get => _operationalDraftAbandonmentColor;
        set
        {
            if (_operationalDraftAbandonmentColor != value)
            {
                _operationalDraftAbandonmentColor = value;
                OnPropertyChanged();
            }
        }
    }

    public Color OperationalVoidAuditColor
    {
        get => _operationalVoidAuditColor;
        set
        {
            if (_operationalVoidAuditColor != value)
            {
                _operationalVoidAuditColor = value;
                OnPropertyChanged();
            }
        }
    }

    public Color CashDrawerOpensColor
    {
        get => _cashDrawerOpensColor;
        set
        {
            if (_cashDrawerOpensColor != value)
            {
                _cashDrawerOpensColor = value;
                OnPropertyChanged();
            }
        }
    }

    public Color CashDrawerFailuresColor
    {
        get => _cashDrawerFailuresColor;
        set
        {
            if (_cashDrawerFailuresColor != value)
            {
                _cashDrawerFailuresColor = value;
                OnPropertyChanged();
            }
        }
    }

    public Color DiscountEventsColor
    {
        get => _discountEventsColor;
        set
        {
            if (_discountEventsColor != value)
            {
                _discountEventsColor = value;
                OnPropertyChanged();
            }
        }
    }

    public Color DiscountTotalColor
    {
        get => _discountTotalColor;
        set
        {
            if (_discountTotalColor != value)
            {
                _discountTotalColor = value;
                OnPropertyChanged();
            }
        }
    }

    public string TrendInsightText
    {
        get => _trendInsightText;
        set
        {
            if (_trendInsightText != value)
            {
                _trendInsightText = value;
                OnPropertyChanged();
            }
        }
    }

    public Color TrendInsightColor
    {
        get => _trendInsightColor;
        set
        {
            if (_trendInsightColor != value)
            {
                _trendInsightColor = value;
                OnPropertyChanged();
            }
        }
    }

    public ReportPage()
    {
        InitializeComponent();

        _authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
        _roleAccessService = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
        _permissionService = ServiceHelper.GetService<PermissionService>()
            ?? new PermissionService(new DatabaseService(), _authService);
        _reportService = ServiceHelper.GetService<DailyReportService>() ?? new DailyReportService(new DatabaseService());
        _reportGenerationService = ServiceHelper.GetService<ReportGenerationService>() ?? new ReportGenerationService(new DatabaseService());
        _reportHistoryService = ServiceHelper.GetService<ReportHistoryService>() ?? new ReportHistoryService(new DatabaseService());
        _businessSettingsService = ServiceHelper.GetService<BusinessSettingsService>() ?? new BusinessSettingsService();
        _cashDrawerService = ServiceHelper.GetService<CashDrawerService>()
            ?? new CashDrawerService(
                new DatabaseService(),
                ServiceHelper.GetService<NetworkPrinterService>() ?? new NetworkPrinterService(),
                _authService);
        _discountAuditService = ServiceHelper.GetService<DiscountAuditService>()
            ?? new DiscountAuditService(new DatabaseService(), _authService);
        _tillExpenseService = ServiceHelper.GetService<TillExpenseService>()
            ?? new TillExpenseService(new DatabaseService(), _authService);
        _zReportService = ServiceHelper.GetService<ZReportService>()
            ?? new ZReportService(
                new DatabaseService(),
                _reportService,
                _tillExpenseService,
                _discountAuditService,
                _businessSettingsService);
        _orderWebDailyReportSyncService = ServiceHelper.GetService<OrderWebDailyReportSyncService>()
            ?? new OrderWebDailyReportSyncService(
                new DatabaseService(),
                _zReportService,
                ServiceHelper.GetService<TimeClockService>() ?? new TimeClockService(new DatabaseService()));
        _zReportPrintService = ServiceHelper.GetService<ZReportPrintService>()
            ?? new ZReportPrintService(
                _zReportService,
                new NetworkPrinterDatabaseService(new DatabaseService()),
                new NetworkPrinterService());
        _timeClockService = ServiceHelper.GetService<TimeClockService>() ?? new TimeClockService(new DatabaseService());

        StaffHoursRows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasStaffHoursRows));
        };

        BindingContext = this;
        TopBar.SetPageTitle("Report");

        TopItems.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(IsTopItemsEmpty));
        };

        DailyTrend.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasDailyTrend));
        };

        VoidAudits.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasVoidAudits));
        };

        CashDrawerAudits.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasCashDrawerAudits));
        };

        TillExpenses.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasTillExpenses));
        };

        DiscountAudits.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasDiscountAudits));
        };

        VoidCancelledOrders.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasVoidCancelledOrders));
        };

        HistoricalReports.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasHistoricalReports));
        };

        ApplyPreset(ReportDatePreset.Today, false);
        UpdatePresetButtonStyles();
        UpdateTopSellRangeButtonStyles();
        UpdateTopSellSectionButtonStyles();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SubscribeToRefreshEvents();

        if (!await SessionAccessGuard.RequireSignedInAsync(_authService))
        {
            return;
        }

        if (!await CanAccessReportsAsync())
        {
            await AppAlertService.ShowAlertAsync("Access Denied", "Only Admin can access Reports.");
            await NavigationCoordinator.Shared.NavigateShellAsync(_roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role));
            return;
        }

        UpdateZReportPrintButtonState();

        if (!_hasLoaded)
        {
            _hasLoaded = true;
            await LoadReportAsync();
            _ = LoadReportSupportDataInBackgroundAsync(loadHistory: CanUseFullReportTools);
        }
        else
        {
            _ = LoadReportSupportDataInBackgroundAsync(loadHistory: false);
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        UnsubscribeFromRefreshEvents();
    }

    private void SubscribeToRefreshEvents()
    {
        if (_isSubscribedToRefreshEvents)
        {
            return;
        }

        AppDataRefreshService.DataChanged += OnAppDataChanged;
        _isSubscribedToRefreshEvents = true;
    }

    private void UnsubscribeFromRefreshEvents()
    {
        if (!_isSubscribedToRefreshEvents)
        {
            return;
        }

        AppDataRefreshService.DataChanged -= OnAppDataChanged;
        _isSubscribedToRefreshEvents = false;
    }

    private void UpdateZReportPrintButtonState()
    {
        var currentRole = _authService.CurrentUser?.Role;
        var canPrint = _roleAccessService.CanPrintZReport(currentRole)
            && (currentRole != UserRole.Cashier || CashierCapabilities.IsGrantedTo(UserRole.Cashier, CashierCapabilities.PrintZ))
            && TerminalRoleService.CanPrintZReport;
        ZReportPrintTodayButton.IsEnabled = canPrint;
        ZReportPrintTodayButton.Opacity = canPrint ? 1 : 0.5;
    }

    private async void OnPrintZReportTodayClicked(object sender, EventArgs e)
    {
        if (!_roleAccessService.CanPrintZReport(_authService.CurrentUser?.Role))
        {
            await AppAlertService.ShowAlertAsync("Access Denied", "Only Admin can print Z-Reports.");
            return;
        }

        if (_authService.CurrentUser?.Role == UserRole.Cashier &&
            !await _permissionService.HasCashierCapabilityAsync(CashierCapabilities.PrintZ))
        {
            await AppAlertService.ShowAlertAsync("Access Denied", "Your Cashier account cannot print Z-Reports.");
            return;
        }

        if (!TerminalRoleService.CanPrintZReport)
        {
            await AppAlertService.ShowAlertAsync(
                "Mother Terminal Required",
                "Z-Report printing runs on the mother terminal only. You can still view reports here.");
            return;
        }

        var button = sender as Button;
        try
        {
            if (button != null)
            {
                button.IsEnabled = false;
                button.Text = "Preparing...";
            }

            var user = _authService.CurrentUser;
            var displayName = user == null
                ? "Admin"
                : !string.IsNullOrWhiteSpace(user.Name) ? user.Name : user.Username;
            var snapshot = await _zReportService.GetSummaryAsync(TradingDayHelper.GetBusinessDate(), displayName);
            snapshot.IsReprint = false;

            var confirmDialog = new ModernConfirmDialog();
            confirmDialog.SetConfirm(
                "Print Z-Report",
                $"Print Z-Report for {snapshot.DateDisplay} to the receipt printer? This prints a report only; it does not close the business day.",
                "Print",
                "Cancel",
                "logo",
                "#0F766E");

            if (!await confirmDialog.ShowAsync())
            {
                return;
            }

            // Z Print is intentionally a single compact summary receipt.
            var result = await _zReportPrintService.PrintAsync(
                snapshot,
                includeDetailSlip: false,
                printedByUserId: user?.Id);
            await AppAlertService.ShowAlertAsync(
                result.Success ? "Z-Report Printed" : "Print Failed",
                result.Message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Report page Z-Report print error: {ex.Message}");
            await AppAlertService.ShowAlertAsync("Print Failed", ex.Message);
        }
        finally
        {
            if (button != null)
            {
                button.Text = "Z Print Today";
            }

            UpdateZReportPrintButtonState();
        }
    }

    private async Task<bool> CanAccessReportsAsync()
    {
        if (_authService.CurrentUser?.Role == UserRole.Cashier)
        {
            return await _permissionService.HasCashierCapabilityAsync(CashierCapabilities.ViewFull);
        }

        return await _permissionService.HasPermissionAsync(PermissionKeys.ReportView);
    }

    private async void OnAppDataChanged(object? sender, AppDataChangedEventArgs e)
    {
        if (e.IsFromCurrentTerminal || !e.HasKind(AppDataChangeKind.Orders))
        {
            return;
        }

        await RefreshCurrentReportAsync(loadHistory: false);
    }

    private async Task RefreshCurrentReportAsync(bool loadHistory)
    {
        await LoadReportAsync();
        _ = LoadReportSupportDataInBackgroundAsync(loadHistory && CanUseFullReportTools);
    }

    private async Task LoadReportSupportDataInBackgroundAsync(bool loadHistory)
    {
        try
        {
            if (loadHistory)
            {
                await LoadHistoricalReportsAsync(updateStatusText: false);
            }

            await RefreshOrderWebUploadStateAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Report support data refresh failed: {ex.Message}");
        }
    }

    private async void OnPresetClicked(object sender, EventArgs e)
    {
        if (sender is Button button)
        {
            if (button.Text == "Top Sell Item")
            {
                ApplyReportMode(ReportViewMode.TopSellItems);
                _selectedTopSellSection = TopSellSection.Food;
                TopSellRangeDays = 30;
                ApplyTopSellRangeDates(30);
                IsTopSellCustomRangeVisible = false;
                IsTopSellCustomRangeDirty = false;
                IsVatCustomRangeVisible = false;
                IsVatCustomRangeDirty = false;
                UpdatePresetButtonStyles();
                UpdateTopSellRangeButtonStyles();
                UpdateTopSellSectionButtonStyles();
                await LoadReportAsync();
                return;
            }

            if (button.Text == "Cash Drawer")
            {
                if (!CanUseAuditReports)
                {
                    await ShowMotherReportOnlyAlertAsync();
                    return;
                }

                ApplyReportMode(ReportViewMode.CashDrawer);
                IsTopSellCustomRangeVisible = false;
                IsTopSellCustomRangeDirty = false;
                IsVatCustomRangeVisible = false;
                IsVatCustomRangeDirty = false;
                UpdatePresetButtonStyles();
                await LoadReportAsync();
                return;
            }

            if (button.Text == "Discount Audit")
            {
                if (!CanUseAuditReports)
                {
                    await ShowMotherReportOnlyAlertAsync();
                    return;
                }

                ApplyReportMode(ReportViewMode.DiscountAudit);
                IsTopSellCustomRangeVisible = false;
                IsTopSellCustomRangeDirty = false;
                IsVatCustomRangeVisible = false;
                IsVatCustomRangeDirty = false;
                UpdatePresetButtonStyles();
                await LoadReportAsync();
                return;
            }

            if (button.Text == "Staff Hours")
            {
                if (!CanUseAuditReports)
                {
                    await ShowMotherReportOnlyAlertAsync();
                    return;
                }

                ApplyReportMode(ReportViewMode.StaffHours);
                ApplyPreset(ReportDatePreset.Today, false);
                IsCustomRangeVisible = false;
                IsCustomRangeDirty = false;
                IsTopSellCustomRangeVisible = false;
                IsTopSellCustomRangeDirty = false;
                IsVatCustomRangeVisible = false;
                IsVatCustomRangeDirty = false;
                UpdatePresetButtonStyles();
                await LoadReportAsync();
                return;
            }

            if (button.Text == "Void / Cancelled")
            {
                if (!CanUseAuditReports)
                {
                    await ShowMotherReportOnlyAlertAsync();
                    return;
                }

                ApplyReportMode(ReportViewMode.VoidCancelled);
                IsTopSellCustomRangeVisible = false;
                IsTopSellCustomRangeDirty = false;
                IsVatCustomRangeVisible = false;
                IsVatCustomRangeDirty = false;
                UpdatePresetButtonStyles();
                await LoadReportAsync();
                return;
            }

            if (button.Text == "Historical Reports")
            {
                if (!CanUseFullReportTools)
                {
                    await ShowMotherReportOnlyAlertAsync();
                    return;
                }

                ApplyReportMode(ReportViewMode.HistoricalReports);
                IsCustomRangeVisible = false;
                IsCustomRangeDirty = false;
                IsTopSellCustomRangeVisible = false;
                IsTopSellCustomRangeDirty = false;
                IsVatCustomRangeVisible = false;
                IsVatCustomRangeDirty = false;
                UpdatePresetButtonStyles();
                await LoadHistoricalReportsAsync();
                return;
            }

            if (button.Text == "VAT")
            {
                if (!CanUseFullReportTools)
                {
                    await ShowMotherReportOnlyAlertAsync();
                    return;
                }

                ApplyReportMode(ReportViewMode.Vat);
                VatRangeDays = 30;
                ApplyVatRangeDates(30);
                IsCustomRangeVisible = false;
                IsCustomRangeDirty = false;
                IsTopSellCustomRangeVisible = false;
                IsTopSellCustomRangeDirty = false;
                IsVatCustomRangeVisible = false;
                IsVatCustomRangeDirty = false;
                ClearVatError();
                UpdatePresetButtonStyles();
                UpdateVatRangeButtonStyles();
                RefreshVatPeriodChrome();
                await LoadReportAsync();
                return;
            }

            ApplyReportMode(ReportViewMode.Orders);
            IsVatCustomRangeVisible = false;
            IsVatCustomRangeDirty = false;
            IsTopSellCustomRangeVisible = false;
            IsTopSellCustomRangeDirty = false;

            var preset = button.Text switch
            {
                "Today" => ReportDatePreset.Today,
                "7 Days" => ReportDatePreset.Last7Days,
                "30 Days" => ReportDatePreset.Last30Days,
                _ => ReportDatePreset.Custom
            };

            ApplyPreset(preset, false);
            
            // Show/hide date pickers based on preset selection
            IsCustomRangeVisible = (preset == ReportDatePreset.Custom);

            if (preset == ReportDatePreset.Custom)
            {
                IsCustomRangeDirty = true;
                return;
            }

            IsCustomRangeDirty = false;
            await LoadReportAsync();
        }
    }

    private async void OnFilterSelectionChanged(object sender, EventArgs e)
    {
        if (!IsLoading)
        {
            await LoadReportAsync();
        }
    }

    private async void OnSearchCompleted(object sender, EventArgs e)
    {
        await LoadReportAsync();
    }

    private async void OnOrdersSearchFieldTapped(object sender, TappedEventArgs e)
    {
        if (_isOrdersSearchKeyboardOpen)
        {
            return;
        }

        _isOrdersSearchKeyboardOpen = true;
        try
        {
            OrdersSearchEntry.Unfocus();

            var keyboard = new OrderWeb.SharedUI.Controls.VirtualKeyboardDialog();
            keyboard.SetPrompt("Search orders", "SEARCH");
            keyboard.SetTextMode(OrderWeb.SharedUI.Controls.VirtualKeyboardTextMode.Text);
            keyboard.SetPlaceholder(OrdersSearchEntry.Placeholder);
            keyboard.SetInitialText(SearchText);

            var result = await keyboard.ShowAsync(this);
            if (result == null)
            {
                return;
            }

            SearchText = result.Trim();
            await LoadReportAsync();
        }
        finally
        {
            _isOrdersSearchKeyboardOpen = false;
        }
    }

    private async void OnSearchClicked(object sender, EventArgs e)
    {
        await LoadReportAsync();
    }

    private async void OnTopSellRangeClicked(object sender, EventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (button.Text == "Custom")
        {
            ApplyReportMode(ReportViewMode.TopSellItems);
            IsTopSellCustomRangeVisible = true;
            IsTopSellCustomRangeDirty = true;
            UpdateTopSellRangeButtonStyles();
            return;
        }

        if (!int.TryParse(button.Text?.Replace(" Days", string.Empty), out var days))
        {
            return;
        }

        ApplyReportMode(ReportViewMode.TopSellItems);
        TopSellRangeDays = days;
        ApplyTopSellRangeDates(days);
        IsTopSellCustomRangeVisible = false;
        IsTopSellCustomRangeDirty = false;
        UpdatePresetButtonStyles();
        UpdateTopSellRangeButtonStyles();
        UpdateTopSellSectionButtonStyles();
        await LoadReportAsync();
    }

    private async void OnTopSellSectionClicked(object sender, EventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        _selectedTopSellSection = button.Text == "Drink"
            ? TopSellSection.Drink
            : TopSellSection.Food;

        OnPropertyChanged(nameof(TopSellInsightText));
        ApplyReportMode(ReportViewMode.TopSellItems);
        UpdateTopSellSectionButtonStyles();
        UpdatePresetButtonStyles();
        await LoadReportAsync();
    }

    private async void OnTopSellCustomSubmitClicked(object sender, EventArgs e)
    {
        if (!CanSubmitTopSellCustomRange)
        {
            return;
        }

        if (EndDate < StartDate)
        {
            await AppAlertService.ShowAlertAsync("Invalid Range", "End date must be on or after start date.");
            return;
        }

        IsTopSellCustomRangeDirty = false;
        await LoadReportAsync();
    }

    private async void OnVatRangeClicked(object sender, EventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        if (!CanUseFullReportTools)
        {
            await ShowMotherReportOnlyAlertAsync();
            return;
        }

        ApplyReportMode(ReportViewMode.Vat);
        IsCustomRangeVisible = false;
        IsCustomRangeDirty = false;
        IsTopSellCustomRangeVisible = false;
        IsTopSellCustomRangeDirty = false;

        if (button.Text == "Custom")
        {
            IsVatCustomRangeVisible = true;
            IsVatCustomRangeDirty = true;
            ClearVatError();
            UpdatePresetButtonStyles();
            UpdateVatRangeButtonStyles();
            RefreshVatPeriodChrome();
            return;
        }

        if (!int.TryParse(button.Text?.Replace(" Days", string.Empty, StringComparison.OrdinalIgnoreCase), out var days)
            || days <= 0)
        {
            SetVatError("Choose a valid VAT period.");
            return;
        }

        VatRangeDays = days;
        ApplyVatRangeDates(days);
        IsVatCustomRangeVisible = false;
        IsVatCustomRangeDirty = false;
        ClearVatError();
        UpdatePresetButtonStyles();
        UpdateVatRangeButtonStyles();
        RefreshVatPeriodChrome();
        await LoadReportAsync();
    }

    private async void OnVatCustomSubmitClicked(object sender, EventArgs e)
    {
        if (!CanSubmitVatCustomRange)
        {
            return;
        }

        if (EndDate < StartDate)
        {
            SetVatError("End date must be on or after start date.");
            await AppAlertService.ShowAlertAsync("Invalid Range", "End date must be on or after start date.");
            return;
        }

        ClearVatError();
        IsVatCustomRangeDirty = false;
        RefreshVatPeriodChrome();
        await LoadReportAsync();
    }

    private async void OnCustomDateChanged(object sender, DateChangedEventArgs e)
    {
        if (IsCustomRangeVisible)
        {
            IsCustomRangeDirty = true;
        }
    }

    private async void OnSubmitCustomRangeClicked(object sender, EventArgs e)
    {
        if (!CanSubmitCustomRange)
        {
            return;
        }

        IsCustomRangeDirty = false;
        await LoadReportAsync();
    }

    private void OnStartDateClicked(object sender, EventArgs e)
    {
        OpenCalendarPopup("Select Start Date", StartDate, CalendarTarget.Start);
    }

    private void OnEndDateClicked(object sender, EventArgs e)
    {
        OpenCalendarPopup("Select End Date", EndDate, CalendarTarget.End);
    }

    private void OpenCalendarPopup(string title, DateTime initialDate, CalendarTarget target)
    {
        _calendarTarget = target;
        _calendarDraftDate = initialDate.Date;
        _calendarDisplayedMonth = new DateTime(initialDate.Year, initialDate.Month, 1);
        CalendarPopupTitle = title;
        RenderCalendar();
        IsCalendarPopupVisible = true;
    }

    private void OnPrevCalendarMonthClicked(object sender, EventArgs e)
    {
        _calendarDisplayedMonth = _calendarDisplayedMonth.AddMonths(-1);
        RenderCalendar();
    }

    private void OnNextCalendarMonthClicked(object sender, EventArgs e)
    {
        _calendarDisplayedMonth = _calendarDisplayedMonth.AddMonths(1);
        RenderCalendar();
    }

    private void OnCancelCalendarPopupClicked(object sender, EventArgs e)
    {
        IsCalendarPopupVisible = false;
    }

    private async void OnApplyCalendarPopupClicked(object sender, EventArgs e)
    {
        IsCalendarPopupVisible = false;

        if (_calendarTarget == CalendarTarget.Start)
        {
            StartDate = _calendarDraftDate;
            if (StartDate > EndDate)
            {
                EndDate = StartDate.AddDays(7);
            }
        }
        else
        {
            EndDate = _calendarDraftDate;
            if (EndDate < StartDate)
            {
                StartDate = EndDate.AddDays(-7);
            }
        }

        if (IsCustomRangeVisible)
        {
            IsCustomRangeDirty = true;
        }
        else if (IsTopSellCustomRangeVisible)
        {
            IsTopSellCustomRangeDirty = true;
            OnPropertyChanged(nameof(TopSellInsightText));
        }
        else if (IsVatCustomRangeVisible)
        {
            IsVatCustomRangeDirty = true;
            ClearVatError();
            RefreshVatPeriodChrome();
        }
        else
        {
            await LoadReportAsync();
        }
    }

    private void ApplyReportMode(ReportViewMode mode)
    {
        if (_reportViewMode == mode)
        {
            return;
        }

        _reportViewMode = mode;
        OnPropertyChanged(nameof(IsOrdersReportVisible));
        OnPropertyChanged(nameof(IsHistoricalReportsVisible));
        OnPropertyChanged(nameof(IsTopSellReportVisible));
        OnPropertyChanged(nameof(IsVatReportVisible));
        OnPropertyChanged(nameof(IsCashDrawerReportVisible));
        OnPropertyChanged(nameof(IsDiscountAuditReportVisible));
        OnPropertyChanged(nameof(IsStaffHoursReportVisible));
        OnPropertyChanged(nameof(IsVoidCancelledReportVisible));
        UpdatePresetButtonStyles();
    }

    private void ApplyTopSellRangeDates(int days)
    {
        var today = DateTime.Today;
        StartDate = today.AddDays(-(days - 1));
        EndDate = today;
        IsCustomRangeVisible = false;
        IsCustomRangeDirty = false;
        OnPropertyChanged(nameof(TopSellInsightText));
    }

    private void ApplyVatRangeDates(int days)
    {
        var today = DateTime.Today;
        StartDate = today.AddDays(-(days - 1));
        EndDate = today;
        IsCustomRangeVisible = false;
        IsCustomRangeDirty = false;
        RefreshVatPeriodChrome();
    }

    private void RefreshVatPeriodChrome(bool resetTotals = true)
    {
        var dayCount = Math.Max(1, (EndDate.Date - StartDate.Date).Days + 1);
        VatPeriodLabel = $"{StartDate:d MMM yyyy} – {EndDate:d MMM yyyy} · {dayCount} day{(dayCount == 1 ? string.Empty : "s")}";

        if (IsVatCustomRangeVisible && IsVatCustomRangeDirty)
        {
            VatStatusText = "Pick start and end dates, then tap Submit.";
            HasVatZeroTaxWarning = false;
            VatZeroTaxWarningText = string.Empty;
            VatRateBandsInsightText = string.Empty;
            VatRateBands.Clear();
            OnPropertyChanged(nameof(HasVatRateBands));
            _lastVatPeriod = null;
            OnPropertyChanged(nameof(CanExportVatSummary));
            if (resetTotals)
            {
                VatHeroText = "—";
                VatGrossText = "—";
                VatNetText = "—";
            }

            return;
        }

        if (resetTotals)
        {
            VatHeroText = "—";
            VatGrossText = "—";
            VatNetText = "—";
            VatStatusText = "Loading VAT collected for this period…";
            HasVatZeroTaxWarning = false;
            VatZeroTaxWarningText = string.Empty;
            VatRateBandsInsightText = string.Empty;
            VatRateBands.Clear();
            OnPropertyChanged(nameof(HasVatRateBands));
            _lastVatPeriod = null;
            OnPropertyChanged(nameof(CanExportVatSummary));
        }
    }

    private void ApplyVatSummary(VatPeriodSnapshot period)
    {
        _lastVatPeriod = period;
        RefreshVatPeriodChrome(resetTotals: false);
        VatPeriodLabel = period.PeriodLabel;
        VatHeroText = $"£{period.Summary.VatAmount:F2}";
        VatGrossText = $"£{period.Summary.GrossSales:F2}";
        VatNetText = $"£{period.Summary.NetSales:F2}";

        VatRateBands.Clear();
        foreach (var band in period.RateBands)
        {
            VatRateBands.Add(band);
        }
        OnPropertyChanged(nameof(HasVatRateBands));

        if (period.Summary.OrderCount <= 0 && period.Summary.VatAmount == 0m && period.Summary.GrossSales == 0m)
        {
            VatStatusText = "No paid-order sales in this period. VAT collected is £0.00.";
            VatRateBandsInsightText = string.Empty;
        }
        else
        {
            VatStatusText =
                $"VAT collected from {period.Summary.OrderCount} paid local POS order{(period.Summary.OrderCount == 1 ? string.Empty : "s")}. " +
                "Local POS only (same totals as cloud VAT → Sales → POS / 3 AM upload). Web orders stay on cloud Online.";

            VatRateBandsInsightText = period.RateBandsExplainTotal
                ? $"Rate bands total £{period.RateBandsVatTotal:F2} — matches VAT collected."
                : $"Rate bands total £{period.RateBandsVatTotal:F2} vs VAT collected £{period.Summary.VatAmount:F2}. Small difference can come from unmapped lines or rounding.";
        }

        if (period.ShouldWarnZeroTax)
        {
            HasVatZeroTaxWarning = true;
            VatZeroTaxWarningText =
                $"{period.ZeroTaxOrderCount} of {period.Summary.OrderCount} paid orders have £0 tax recorded. " +
                "Totals may understate VAT due if tax was not saved on those tills.";
        }
        else
        {
            HasVatZeroTaxWarning = false;
            VatZeroTaxWarningText = string.Empty;
        }

        OnPropertyChanged(nameof(CanExportVatSummary));
    }

    private void ClearVatError()
    {
        HasVatError = false;
        VatErrorText = string.Empty;
        OnPropertyChanged(nameof(CanExportVatSummary));
    }

    private void SetVatError(string message)
    {
        HasVatError = true;
        VatErrorText = message;
        VatStatusText = string.Empty;
        VatHeroText = "—";
        VatGrossText = "—";
        VatNetText = "—";
        HasVatZeroTaxWarning = false;
        VatZeroTaxWarningText = string.Empty;
        VatRateBandsInsightText = string.Empty;
        VatRateBands.Clear();
        OnPropertyChanged(nameof(HasVatRateBands));
        _lastVatPeriod = null;
        OnPropertyChanged(nameof(CanExportVatSummary));
    }

    private static string FormatFriendlyReportError(Exception ex)
    {
        var raw = ex.Message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Something went wrong loading this report. Please try again.";
        }

        var lower = raw.ToLowerInvariant();
        if (lower.Contains("unknown column")
            || lower.Contains("sql syntax")
            || lower.Contains("doesn't exist")
            || lower.Contains("does not exist")
            || (lower.Contains("table") && lower.Contains("exist")))
        {
            return "We couldn’t prepare the VAT figures for this date range. Tap VAT again, or restart Mother POS. If it keeps happening, contact support.";
        }

        if (lower.Contains("unable to connect")
            || lower.Contains("timeout")
            || lower.Contains("connection"))
        {
            return "Mother POS couldn’t reach the database. Check that MariaDB is running, then try again.";
        }

        // Keep short; avoid dumping long SQL to staff.
        return raw.Length > 160 ? raw[..157] + "…" : raw;
    }

    private async void OnVatExportCsvClicked(object sender, EventArgs e)
    {
        if (!CanExportVatSummary)
        {
            if (!CanExportReports)
            {
                await ShowMotherReportOnlyAlertAsync();
            }

            return;
        }

        try
        {
            var period = _lastVatPeriod ?? await _reportService.GetVatPeriodAsync(StartDate, EndDate);
            var filePath = await _reportService.ExportVatCsvAsync(period);
            await OpenExportedFileAsync(filePath, "VAT CSV Exported");
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("VAT CSV Export Failed", ex.Message);
        }
    }

    private async void OnVatExportPdfClicked(object sender, EventArgs e)
    {
        if (!CanExportVatSummary)
        {
            if (!CanExportReports)
            {
                await ShowMotherReportOnlyAlertAsync();
            }

            return;
        }

        try
        {
            var period = _lastVatPeriod ?? await _reportService.GetVatPeriodAsync(StartDate, EndDate);
            var businessInfo = await _businessSettingsService.GetBusinessInfoAsync();
            var filePath = await _reportService.ExportVatPdfAsync(period, businessInfo, "POS-in-NET");
            await OpenExportedFileAsync(filePath, "VAT PDF Exported");
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("VAT PDF Export Failed", ex.Message);
        }
    }

    private void UpdateTopSellRangeButtonStyles()
    {
        ApplyPresetStyle(TopSellThirtyDaysButton, TopSellRangeDays == 30);
        ApplyPresetStyle(TopSellSixtyDaysButton, TopSellRangeDays == 60);
        ApplyPresetStyle(TopSellNinetyDaysButton, TopSellRangeDays == 90);
        ApplyPresetStyle(TopSellCustomRangeButton, IsTopSellCustomRangeVisible);
    }

    private void UpdateVatRangeButtonStyles()
    {
        ApplyPresetStyle(VatSevenDaysButton, !IsVatCustomRangeVisible && VatRangeDays == 7);
        ApplyPresetStyle(VatFifteenDaysButton, !IsVatCustomRangeVisible && VatRangeDays == 15);
        ApplyPresetStyle(VatThirtyDaysButton, !IsVatCustomRangeVisible && VatRangeDays == 30);
        ApplyPresetStyle(VatOneEightyDaysButton, !IsVatCustomRangeVisible && VatRangeDays == 180);
        ApplyPresetStyle(VatThreeSixtyFiveDaysButton, !IsVatCustomRangeVisible && VatRangeDays == 365);
        ApplyPresetStyle(VatCustomRangeButton, IsVatCustomRangeVisible);
        ApplyPresetStyle(VatReportButton, _reportViewMode == ReportViewMode.Vat);
    }

    private void UpdateTopSellSectionButtonStyles()
    {
        ApplyPresetStyle(TopSellFoodButton, _selectedTopSellSection == TopSellSection.Food);
        ApplyPresetStyle(TopSellDrinkButton, _selectedTopSellSection == TopSellSection.Drink);
    }

    private void RenderCalendar()
    {
        CalendarMonthLabel = _calendarDisplayedMonth.ToString("MMMM yyyy");
        CalendarDaysHost.Content = CreateCalendarView(_calendarDisplayedMonth, selectedDate =>
        {
            _calendarDraftDate = selectedDate;
            RenderCalendar();
        });
    }

    private View CreateCalendarView(DateTime currentMonth, Action<DateTime> onDateSelected)
    {
        var container = new VerticalStackLayout
        {
            Spacing = 12,
            Padding = 20
        };

        // Days of week header
        var daysHeaderLayout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection 
            { 
                new ColumnDefinition(),
                new ColumnDefinition(),
                new ColumnDefinition(),
                new ColumnDefinition(),
                new ColumnDefinition(),
                new ColumnDefinition(),
                new ColumnDefinition()
            },
            ColumnSpacing = 6,
            RowSpacing = 8
        };

        var dayNames = new[] { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" };
        for (int i = 0; i < 7; i++)
        {
            daysHeaderLayout.Add(
                new Label
                {
                    Text = dayNames[i],
                    FontFamily = "OpenSansSemibold",
                    FontSize = 12,
                    TextColor = Color.FromArgb(i == 6 ? "#7C3AED" : "#64748B"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    HeightRequest = 28
                },
                i, 0
            );
        }
        container.Add(daysHeaderLayout);

        // Calendar days grid
        var calendarGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection 
            { 
                new ColumnDefinition(),
                new ColumnDefinition(),
                new ColumnDefinition(),
                new ColumnDefinition(),
                new ColumnDefinition(),
                new ColumnDefinition(),
                new ColumnDefinition()
            },
            ColumnSpacing = 6,
            RowSpacing = 8
        };

        var firstDay = new DateTime(currentMonth.Year, currentMonth.Month, 1);
        int dayOfWeek = (int)firstDay.DayOfWeek;

        var daysInMonth = DateTime.DaysInMonth(currentMonth.Year, currentMonth.Month);
        int column = dayOfWeek;
        int row = 0;

        for (int day = 1; day <= daysInMonth; day++)
        {
            var dayDate = new DateTime(currentMonth.Year, currentMonth.Month, day);
            var isSelected = dayDate.Date == _calendarDraftDate.Date;

            var dayButton = new Button
            {
                Text = day.ToString(),
                BackgroundColor = isSelected ? Color.FromArgb("#10B981") : Colors.Transparent,
                TextColor = isSelected ? Colors.White : Color.FromArgb("#0F172A"),
                CornerRadius = 8,
                FontFamily = "OpenSansRegular",
                FontSize = 14,
                Padding = 0,
                Margin = 0,
                HeightRequest = 40,
                BorderColor = Colors.Transparent,
                BorderWidth = 0
            };

            dayButton.Clicked += (s, e) =>
            {
                onDateSelected(dayDate);
            };

            calendarGrid.Add(dayButton, column, row);

            column++;
            if (column > 6)
            {
                column = 0;
                row++;
            }
        }

        container.Add(calendarGrid);
        return container;
    }

    private async void OnExportCsvClicked(object sender, EventArgs e)
    {
        if (!CanExportReports)
        {
            await ShowMotherReportOnlyAlertAsync();
            return;
        }

        if (IsLoading)
        {
            return;
        }

        try
        {
            if (_reportViewMode == ReportViewMode.StaffHours)
            {
                var csv = await _timeClockService.ExportLabourCsvAsync(StartDate, EndDate);
                var fileName = $"staff-hours-{StartDate:yyyyMMdd}-{EndDate:yyyyMMdd}.csv";
                var staffHoursFilePath = Path.Combine(FileSystem.CacheDirectory, fileName);
                await File.WriteAllTextAsync(staffHoursFilePath, csv);
                await OpenExportedFileAsync(staffHoursFilePath, "Staff Hours CSV Exported");
                return;
            }

            var report = await LoadSnapshotAsync();
            if (report == null)
            {
                return;
            }

            var filePath = await _reportService.ExportCsvAsync(report);
            await OpenExportedFileAsync(filePath, "CSV Exported");
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("CSV Export Failed", ex.Message);
        }
    }

    private async void OnExportPdfClicked(object sender, EventArgs e)
    {
        if (!CanExportReports)
        {
            await ShowMotherReportOnlyAlertAsync();
            return;
        }

        if (IsLoading)
        {
            return;
        }

        try
        {
            var report = await LoadSnapshotAsync();
            if (report == null)
            {
                return;
            }

            var businessInfo = await _businessSettingsService.GetBusinessInfoAsync();
            var filePath = await _reportService.ExportPdfAsync(report, businessInfo, "POS-in-NET");
            await OpenExportedFileAsync(filePath, "PDF Exported");
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("PDF Export Failed", ex.Message);
        }
    }

    private static async Task OpenExportedFileAsync(string filePath, string title)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists || fileInfo.Length == 0)
        {
            throw new InvalidOperationException("Export file could not be created. Please try again.");
        }

        try
        {
            await using var sourceStream = File.OpenRead(filePath);
            var saveResult = await FileSaver.Default.SaveAsync(fileInfo.Name, sourceStream, CancellationToken.None);

            if (saveResult.IsSuccessful)
            {
                await AppAlertService.ShowAlertAsync(title, $"Saved to:\n{saveResult.FilePath}");
                return;
            }
        }
        catch
        {
            // Fall back to share/open flows below.
        }

        try
        {
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = title,
                File = new ShareFile(filePath)
            });
        }
        catch
        {
            // If share dialog is unavailable, fall back to opening the generated file directly.
        }

        try
        {
            await Launcher.Default.OpenAsync(new OpenFileRequest
            {
                Title = title,
                File = new ReadOnlyFile(filePath)
            });
        }
        catch
        {
            // Ignore open failures and still show save location below.
        }

        await AppAlertService.ShowAlertAsync(title, $"Saved to:\n{filePath}");
    }

    private async void OnOrderSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ReportOrderRow selectedOrder)
        {
            return;
        }

        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }

        if (!CanUseFullReportTools)
        {
            await ShowMotherReportOnlyAlertAsync();
            return;
        }

        try
        {
            await NavigationCoordinator.Shared.NavigateTemporaryRouteAsync($"reportdetails?orderDbId={selectedOrder.OrderDbId}");
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Navigation Error", ex.Message);
        }
    }

    private async void OnHardDeleteOrderClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not ReportOrderRow order)
        {
            await AppAlertService.ShowAlertAsync("Order Error", "Unable to identify the selected order.");
            return;
        }

        if (!CanUseFullReportTools)
        {
            await ShowMotherReportOnlyAlertAsync();
            return;
        }

        var currentUser = _authService?.CurrentUser;
        if (currentUser is not { IsActive: true, Role: UserRole.Admin })
        {
            await AppAlertService.ShowAlertAsync("Administrator Required", "Only a signed-in Administrator can void an order from Report.");
            return;
        }

        if (!string.Equals(order.SourceChannel, "local", StringComparison.OrdinalIgnoreCase))
        {
            await AppAlertService.ShowAlertAsync(
                "OrderWeb Record",
                "Only local (original till) orders can be voided here. Web / OrderWeb orders stay unchanged.");
            return;
        }

        var orderLabel = string.IsNullOrWhiteSpace(order.OrderNumber) ? order.OrderId : order.OrderNumber;
        var confirm = await DisplayAlert(
            "Void order?",
            $"Void order {orderLabel}?",
            "Yes",
            "No");

        if (!confirm)
        {
            return;
        }

        try
        {
            button.IsEnabled = false;
            var voidedByName = !string.IsNullOrWhiteSpace(currentUser.Name) ? currentUser.Name : currentUser.Username;
            var voided = await _reportService.SoftVoidLocalOrderAsync(
                order.OrderDbId,
                currentUser.Id,
                voidedByName);

            if (!voided)
            {
                await AppAlertService.ShowAlertAsync("Not Found", $"Order {orderLabel} was not found.");
                return;
            }

            ApplyReportMode(ReportViewMode.VoidCancelled);
            UpdatePresetButtonStyles();
            await LoadReportAsync();
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Void Failed", FormatFriendlyReportError(ex));
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void OnPermanentDeleteVoidOrderClicked(object sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not ReportVoidCancelledRow order)
        {
            await AppAlertService.ShowAlertAsync("Order Error", "Unable to identify the selected order.");
            return;
        }

        if (!CanPermanentlyDeleteVoidOrders)
        {
            await AppAlertService.ShowAlertAsync(
                "Administrator Required",
                "Only a signed-in Administrator can permanently delete a voided order.");
            return;
        }

        var currentUser = _authService?.CurrentUser;
        if (currentUser is not { IsActive: true, Role: UserRole.Admin })
        {
            await AppAlertService.ShowAlertAsync(
                "Administrator Required",
                "Only a signed-in Administrator can permanently delete a voided order.");
            return;
        }

        if (!string.Equals(order.SourceChannel, "local", StringComparison.OrdinalIgnoreCase))
        {
            await AppAlertService.ShowAlertAsync(
                "OrderWeb Record",
                "Only local (original till) voided orders can be deleted here. Web / OrderWeb orders stay unchanged.");
            return;
        }

        var orderLabel = string.IsNullOrWhiteSpace(order.OrderNumber) ? order.OrderId : order.OrderNumber;
        var confirm = await DisplayAlert(
            "Delete permanently?",
            $"Permanently delete voided order {orderLabel} from this till?\n\nThis removes it from the database and cannot be undone. Allowed only before that day’s report is uploaded to OrderWeb.",
            "Yes",
            "No");

        if (!confirm)
        {
            return;
        }

        try
        {
            button.IsEnabled = false;
            var deletedByName = !string.IsNullOrWhiteSpace(currentUser.Name) ? currentUser.Name : currentUser.Username;
            var deleted = await _reportService.HardDeleteOrderAsync(
                order.OrderDbId,
                currentUser.Id,
                deletedByName,
                "Permanent delete from Void audit");

            if (!deleted)
            {
                await AppAlertService.ShowAlertAsync("Not Found", $"Order {orderLabel} was not found.");
                return;
            }

            await AppAlertService.ShowAlertAsync("Order deleted", $"Voided order {orderLabel} was permanently removed from this till.");
            await LoadReportAsync();
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Delete failed", FormatFriendlyReportError(ex));
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void ApplyPreset(ReportDatePreset preset, bool loadImmediately = true)
    {
        var today = DateTime.Today;
        _selectedPreset = preset;

        IsCustomRangeVisible = preset == ReportDatePreset.Custom;
        UpdatePresetButtonStyles();

        switch (preset)
        {
            case ReportDatePreset.Today:
                StartDate = today;
                EndDate = today;
                break;
            case ReportDatePreset.Last7Days:
                StartDate = today.AddDays(-6);
                EndDate = today;
                break;
            case ReportDatePreset.Last30Days:
                StartDate = today.AddDays(-29);
                EndDate = today;
                break;
            case ReportDatePreset.Custom:
                StartDate = StartDate == default ? today.AddDays(-6) : StartDate;
                EndDate = EndDate == default ? today : EndDate;
                break;
        }

        if (loadImmediately)
        {
            _ = LoadReportAsync();
        }
    }

    private void UpdatePresetButtonStyles()
    {
        var isOrdersMode = _reportViewMode == ReportViewMode.Orders;

        ApplyPresetStyle(TodayPresetButton, isOrdersMode && _selectedPreset == ReportDatePreset.Today);
        ApplyPresetStyle(SevenDaysPresetButton, isOrdersMode && _selectedPreset == ReportDatePreset.Last7Days);
        ApplyPresetStyle(ThirtyDaysPresetButton, isOrdersMode && _selectedPreset == ReportDatePreset.Last30Days);
        ApplyPresetStyle(CustomPresetButton, isOrdersMode && _selectedPreset == ReportDatePreset.Custom);
        ApplyPresetStyle(TopSellItemButton, _reportViewMode == ReportViewMode.TopSellItems);
        ApplyPresetStyle(CashDrawerButton, _reportViewMode == ReportViewMode.CashDrawer);
        ApplyPresetStyle(DiscountAuditButton, _reportViewMode == ReportViewMode.DiscountAudit);
        ApplyPresetStyle(StaffHoursButton, _reportViewMode == ReportViewMode.StaffHours);
        ApplyPresetStyle(VoidCancelledButton, _reportViewMode == ReportViewMode.VoidCancelled);
        ApplyPresetStyle(VatReportButton, _reportViewMode == ReportViewMode.Vat);
        ApplyPresetStyle(HistoricalReportsButton, _reportViewMode == ReportViewMode.HistoricalReports);
        if (_reportViewMode == ReportViewMode.Vat)
        {
            UpdateVatRangeButtonStyles();
        }
    }

    private static void ApplyPresetStyle(Button? button, bool isActive)
    {
        if (button == null)
        {
            return;
        }

        if (isActive)
        {
            button.BackgroundColor = Color.FromArgb("#0369A1");
            button.TextColor = Colors.White;
            button.BorderColor = Color.FromArgb("#0369A1");
            button.BorderWidth = 1;
        }
        else
        {
            button.BackgroundColor = Color.FromArgb("#EEF2F7");
            button.TextColor = Color.FromArgb("#0F172A");
            button.BorderColor = Color.FromArgb("#D8E1EC");
            button.BorderWidth = 1;
        }
    }

    private async Task LoadReportAsync()
    {
        if (IsLoading)
        {
            return;
        }

        var performance = PosPerformanceMonitor.BeginDataLoad("Reports");
        IsLoading = true;

        try
        {
            if (_reportViewMode == ReportViewMode.Vat)
            {
                if (EndDate < StartDate)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        SetVatError("End date must be on or after start date.");
                        LastUpdatedText = "VAT period needs a valid date range";
                    });
                    PosPerformanceMonitor.MarkDataVisible(performance);
                    return;
                }

                if (IsVatCustomRangeVisible && IsVatCustomRangeDirty)
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        ClearVatError();
                        RefreshVatPeriodChrome(resetTotals: true);
                        LastUpdatedText = "VAT custom range ready — tap Submit";
                    });
                    PosPerformanceMonitor.MarkDataVisible(performance);
                    return;
                }

                // v1 lock: always all sources / all order types (matches unfiltered Sales Summary).
                var vatPeriod = await _reportService.GetVatPeriodAsync(StartDate, EndDate);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ClearVatError();
                    ApplyVatSummary(vatPeriod);
                    LastUpdatedText = $"VAT · {StartDate:dd MMM} – {EndDate:dd MMM} · all sources · updated {DateTime.Now:HH:mm:ss}";
                });
                PosPerformanceMonitor.MarkDataVisible(performance);
                return;
            }

            if (_reportViewMode == ReportViewMode.CashDrawer)
            {
                var queryEndDate = EndDate.AddDays(1);
                var cashDrawerRowsTask = _cashDrawerService.GetAuditEntriesAsync(StartDate, queryEndDate);
                var expenseSummaryTask = _tillExpenseService.GetSummaryAsync(StartDate, queryEndDate);
                var expenseRowsTask = _tillExpenseService.GetExpensesAsync(StartDate, queryEndDate);
                var pendingRowsTask = _tillExpenseService.GetPendingShoppingAsync();

                await Task.WhenAll(cashDrawerRowsTask, expenseSummaryTask, expenseRowsTask, pendingRowsTask);

                var cashDrawerRows = await cashDrawerRowsTask;
                var expenseSummary = await expenseSummaryTask;
                var expenseRows = await expenseRowsTask;
                var pendingRows = await pendingRowsTask;
                var lastUpdated = $"Cash drawer & till expenses · {StartDate:dd MMM} – {EndDate:dd MMM} · updated {DateTime.Now:HH:mm:ss}";

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ApplyCashDrawerAudits(cashDrawerRows);
                    ApplyTillExpenses(expenseSummary, expenseRows, pendingRows);
                    LastUpdatedText = lastUpdated;
                });
                PosPerformanceMonitor.MarkDataVisible(performance);
                return;
            }

            if (_reportViewMode == ReportViewMode.DiscountAudit)
            {
                var discountAuditRows = await _discountAuditService.GetAuditEntriesAsync(StartDate, EndDate.AddDays(1));
                var lastUpdated = $"Last updated {DateTime.Now:HH:mm:ss}";

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ApplyDiscountAudits(discountAuditRows);
                    LastUpdatedText = lastUpdated;
                });
                PosPerformanceMonitor.MarkDataVisible(performance);
                return;
            }

            if (_reportViewMode == ReportViewMode.StaffHours)
            {
                var (rows, summary) = await _timeClockService.GetLabourReportAsync(StartDate, EndDate);
                var lastUpdated = $"Staff hours · {StartDate:dd MMM} – {EndDate:dd MMM} · updated {DateTime.Now:HH:mm:ss}";

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ApplyStaffHoursReport(rows, summary);
                    LastUpdatedText = lastUpdated;
                });
                PosPerformanceMonitor.MarkDataVisible(performance);
                return;
            }

            if (_reportViewMode == ReportViewMode.VoidCancelled)
            {
                var audit = await _reportService.GetVoidCancelledReportAsync(StartDate, EndDate, SearchText);
                var lastUpdated = $"Void / cancelled audit · {StartDate:dd MMM} – {EndDate:dd MMM} · updated {DateTime.Now:HH:mm:ss}";

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    ApplyVoidCancelledReport(audit);
                    LastUpdatedText = lastUpdated;
                });
                PosPerformanceMonitor.MarkDataVisible(performance);
                return;
            }

            var reportTask = LoadSnapshotAsync();
            var analyticsTask = _reportService.GetOperationalAnalyticsAsync(StartDate, EndDate.AddDays(1));
            var trendTask = LoadTrendAsync();

            var report = await reportTask;
            if (report == null)
            {
                return;
            }

            object? analytics = await analyticsTask;
            var trendRows = await trendTask;
            var snapshotNote = CanUseFullReportTools && HistoricalReports.Count > 0
                ? $" · {HistoricalReports.Count} saved snapshots"
                : string.Empty;
            var lastUpdatedText = $"Live report · {StartDate:dd MMM} – {EndDate:dd MMM} · updated {DateTime.Now:HH:mm:ss}{snapshotNote}";

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ApplySnapshot(report, analytics);
                ApplyTrend(trendRows);
                LastUpdatedText = lastUpdatedText;
            });
            PosPerformanceMonitor.MarkDataVisible(performance);
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (_reportViewMode == ReportViewMode.Vat)
                {
                    var friendly = FormatFriendlyReportError(ex);
                    SetVatError(friendly);
                    LastUpdatedText = "Couldn’t load VAT";
                    await AppAlertService.ShowAlertAsync("Couldn’t load VAT", friendly);
                    return;
                }

                await AppAlertService.ShowAlertAsync("Report", FormatFriendlyReportError(ex));
            });
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() => IsLoading = false);
        }
    }

    private async Task<DailyReportSnapshot?> LoadSnapshotAsync()
    {
        if (EndDate < StartDate)
        {
            await AppAlertService.ShowAlertAsync("Invalid Range", "End date must be on or after start date.");
            return null;
        }

        if (_reportViewMode == ReportViewMode.TopSellItems)
        {
            return await _reportService.GetTopSellReportAsync(StartDate, EndDate, _selectedTopSellSection, SearchText);
        }

        var sourceFilter = Enum.TryParse<ReportSourceFilter>(SelectedSourceFilter, true, out var sourceValue)
            ? sourceValue
            : ReportSourceFilter.All;

        var orderTypeFilter = Enum.TryParse<ReportOrderTypeFilter>(SelectedOrderTypeFilter, true, out var typeValue)
            ? typeValue
            : ReportOrderTypeFilter.All;

        return await _reportService.GetReportAsync(StartDate, EndDate, SearchText, sourceFilter, orderTypeFilter);
    }

    private void ApplySnapshot(DailyReportSnapshot report, object? analytics)
    {
        Orders.Clear();
        foreach (var order in report.Orders)
        {
            Orders.Add(order);
        }

        TopItems.Clear();
        foreach (var item in report.TopItems)
        {
            TopItems.Add(item);
        }
        OnPropertyChanged(nameof(IsTopItemsEmpty));

        ServiceChargeRemovalAudits.Clear();
        foreach (var audit in report.ServiceChargeRemovalAudits)
        {
            ServiceChargeRemovalAudits.Add(audit);
        }
        OnPropertyChanged(nameof(HasServiceChargeRemovalAudits));

        SummaryOrdersText = report.Summary.OrderCount.ToString(CultureInfo.InvariantCulture);
        SummaryGrossText = $"£{report.Summary.GrossSales:F2}";
        SummaryNetText = $"£{report.Summary.NetSales:F2}";
        SummaryVatText = $"£{report.Summary.VatAmount:F2}";
        SummaryDeliveryText = $"£{report.Summary.DeliveryChargeTotal:F2}";
        SummaryAverageText = $"£{report.Summary.AverageOrderValue:F2}";
        _summaryServiceChargeText = $"£{report.Summary.ServiceChargeTotal:F2}";
        _summaryRemovedChargeText = $"£{report.Summary.RemovedServiceChargeValue:F2} ({report.Summary.RemovedServiceChargeCount})";
        _summaryCashTipsText = $"£{report.Summary.CashTips:F2}";
        _summaryCardTipsText = $"£{report.Summary.CardTips:F2}";
        _summaryRefundsText = $"£{Math.Abs(report.Summary.RefundTotal):F2}";
        _summaryCollectedText = $"£{report.Summary.FinalMoneyCollected:F2}";
        _summaryCashPaidText = $"£{report.Summary.CashTotal:F2}";
        _summaryCardPaidText = $"£{report.Summary.CardTotal:F2}";
        _summaryGiftCardPaidText = $"£{report.Summary.GiftCardTotal:F2}";
        _summaryTotalTipsText = $"£{report.Summary.TotalTips:F2}";
        OnPropertyChanged(nameof(SummaryServiceChargeText));
        OnPropertyChanged(nameof(SummaryRemovedChargeText));
        OnPropertyChanged(nameof(SummaryCashTipsText));
        OnPropertyChanged(nameof(SummaryCardTipsText));
        OnPropertyChanged(nameof(SummaryTotalTipsText));
        OnPropertyChanged(nameof(SummaryRefundsText));
        OnPropertyChanged(nameof(SummaryCollectedText));
        OnPropertyChanged(nameof(SummaryCashPaidText));
        OnPropertyChanged(nameof(SummaryCardPaidText));
        OnPropertyChanged(nameof(SummaryGiftCardPaidText));

        var sendLatencySampleCount = 0;
        var sendLatencyAverageMs = 0d;
        var paymentCompletionSampleCount = 0;
        var paymentCompletionAverageSeconds = 0d;
        var draftAbandonmentCount = 0;
        var voidAuditCount = 0;

        VoidAudits.Clear();
        if (analytics != null)
        {
            var sendLatency = GetNestedMetric(analytics, "SendLatency");
            var paymentCompletion = GetNestedMetric(analytics, "PaymentCompletionTime");
            draftAbandonmentCount = GetIntProperty(analytics, "DraftAbandonmentCount");
            var voidAuditItems = GetEnumerableProperty(analytics, "VoidAudits");
            sendLatencySampleCount = GetIntProperty(sendLatency, "SampleCount");
            sendLatencyAverageMs = GetDoubleProperty(sendLatency, "Average");
            paymentCompletionSampleCount = GetIntProperty(paymentCompletion, "SampleCount");
            paymentCompletionAverageSeconds = GetDoubleProperty(paymentCompletion, "Average");

            OperationalSendLatencyText = sendLatencySampleCount > 0
                ? $"{sendLatencyAverageMs:F0} ms avg"
                : "No samples";
            OperationalPaymentCompletionText = paymentCompletionSampleCount > 0
                ? $"{paymentCompletionAverageSeconds:F0} s avg"
                : "No samples";
            var voidAuditRows = BuildVoidAuditRows(voidAuditItems);
            voidAuditCount = voidAuditRows.Count;
            OperationalVoidAuditText = voidAuditRows.Count > 0
                ? $"{voidAuditRows.Count} void events"
                : "No voids";
            OperationalDraftAbandonmentText = $"{draftAbandonmentCount} drafts";

            foreach (var voidAudit in voidAuditRows.Take(5))
            {
                VoidAudits.Add(voidAudit);
            }
        }
        else
        {
            OperationalSendLatencyText = "No samples";
            OperationalPaymentCompletionText = "No samples";
            OperationalVoidAuditText = "No voids";
            OperationalDraftAbandonmentText = "0 drafts";
        }

        ApplyKpiPalette(
            report.Summary.OrderCount,
            report.Summary.GrossSales,
            report.Summary.NetSales,
            report.Summary.VatAmount,
            report.Summary.DeliveryChargeTotal,
            report.Summary.AverageOrderValue,
            sendLatencySampleCount,
            sendLatencyAverageMs,
            paymentCompletionSampleCount,
            paymentCompletionAverageSeconds,
            draftAbandonmentCount,
            voidAuditCount);

        OnPropertyChanged(nameof(HasVoidAudits));
    }

    private void ApplyCashDrawerAudits(List<CashDrawerAuditEntry> entries)
    {
        CashDrawerAudits.Clear();

        var successCount = entries.Count(entry => entry.Success);
        var failureCount = entries.Count - successCount;

        CashDrawerOpensText = $"{successCount} opens";
        CashDrawerFailuresText = $"{failureCount} failed";
        CashDrawerLastOpenText = entries.Count > 0
            ? $"Last attempt {entries[0].EventAt:dd/MM HH:mm}"
            : "No drawer opens in this range";

        CashDrawerOpensColor = successCount > 0
            ? Color.FromArgb("#0F766E")
            : Color.FromArgb("#64748B");
        CashDrawerFailuresColor = failureCount > 0
            ? Color.FromArgb("#DC2626")
            : Color.FromArgb("#16A34A");

        foreach (var entry in entries.Take(8))
        {
            CashDrawerAudits.Add(new CashDrawerReportRow
            {
                EventAt = entry.EventAt,
                RequestedByName = string.IsNullOrWhiteSpace(entry.RequestedByName) ? "Unknown" : entry.RequestedByName,
                Reason = string.IsNullOrWhiteSpace(entry.Reason) ? "Manual open" : entry.Reason,
                PrinterName = string.IsNullOrWhiteSpace(entry.PrinterName) ? "No printer" : entry.PrinterName,
                StatusDisplay = entry.Success ? "Opened" : "Failed",
                StatusColor = entry.Success ? Color.FromArgb("#16A34A") : Color.FromArgb("#DC2626"),
                DetailDisplay = BuildCashDrawerDetail(entry)
            });
        }

        OnPropertyChanged(nameof(HasCashDrawerAudits));
    }

    private void ApplyStaffHoursReport(List<LabourReportRow> rows, LabourReportSummary summary)
    {
        StaffHoursRows.Clear();
        foreach (var row in rows)
        {
            StaffHoursRows.Add(row);
        }

        StaffHoursTotalText = summary.TotalHoursDisplay;
        StaffHoursStaffCountText = $"{summary.StaffCount} staff";
        StaffHoursOpenText = summary.OpenSessionCount > 0
            ? $"{summary.OpenSessionCount} open shifts"
            : "No open shifts";

        OnPropertyChanged(nameof(HasStaffHoursRows));
    }

    private void ApplyTillExpenses(TillExpenseSummary summary, List<TillExpense> expenses, List<TillExpense> pendingRows)
    {
        TillExpenses.Clear();

        TillExpensePendingText = summary.PendingShoppingCount > 0
            ? $"{summary.PendingShoppingCount} pending · £{summary.PendingShoppingTotal:F2} out"
            : "0 pending";
        TillExpenseNetOutText = $"£{summary.TotalNetOut:F2} out";
        TillExpenseShoppingText = $"£{summary.ShoppingNet:F2}";
        TillExpenseDeliveryText = $"£{summary.DeliveryTotal:F2}";
        TillExpenseRefundText = $"£{summary.RefundTotal:F2}";
        TillExpenseOtherText = $"£{summary.OtherTotal:F2}";
        TillExpenseCashReturnText = $"£{summary.CashReturnTotal:F2} back";

        var cashCounts = expenses.Where(e => e.Category == TillExpenseCategory.CashCount).ToList();
        TillExpenseCashCountText = cashCounts.Count > 0
            ? $"{cashCounts.Count} counts"
            : "0 counts";

        var displayRows = new List<TillExpense>();
        foreach (var pending in pendingRows.OrderBy(e => e.CreatedAt))
        {
            if (!expenses.Any(e => e.Id == pending.Id))
            {
                displayRows.Add(pending);
            }
        }

        displayRows.AddRange(expenses.OrderByDescending(e => e.SettledAt ?? e.CreatedAt));

        foreach (var expense in displayRows.Take(20))
        {
            TillExpenses.Add(new TillExpenseReportRow
            {
                EventAt = expense.SettledAt ?? expense.CreatedAt,
                CategoryDisplay = expense.CategoryDisplay,
                StatusDisplay = expense.StatusDisplay,
                SummaryDisplay = expense.SummaryDisplay,
                RecordedByName = string.IsNullOrWhiteSpace(expense.RecordedByName) ? "Unknown" : expense.RecordedByName,
                AmountDisplay = expense.Category == TillExpenseCategory.CashCount && expense.CountedCash.HasValue
                    ? $"£{expense.CountedCash.Value:F2} counted"
                    : expense.Status == TillExpenseStatus.Pending
                        ? $"£{expense.AmountTaken:F2} out (pending)"
                        : expense.Category == TillExpenseCategory.Shopping && expense.AmountReturned.HasValue && expense.AmountReturned.Value > 0
                            ? $"£{expense.NetAmount:F2} net · £{expense.AmountReturned.Value:F2} returned"
                            : $"£{expense.NetAmount:F2} net"
            });
        }

        OnPropertyChanged(nameof(HasTillExpenses));
    }

    private static string BuildCashDrawerDetail(CashDrawerAuditEntry entry)
    {
        var contextParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(entry.TableNumber))
        {
            contextParts.Add($"Table {entry.TableNumber}");
        }

        if (!string.IsNullOrWhiteSpace(entry.OrderNumber))
        {
            contextParts.Add($"Order {entry.OrderNumber}");
        }

        if (!entry.Success && !string.IsNullOrWhiteSpace(entry.ErrorMessage))
        {
            contextParts.Add(entry.ErrorMessage);
        }

        return contextParts.Count > 0
            ? string.Join(" | ", contextParts)
            : entry.SourceArea;
    }

    private void ApplyDiscountAudits(List<DiscountAuditEntry> entries)
    {
        DiscountAudits.Clear();

        var activeDiscounts = entries.Where(entry => !string.Equals(entry.Action, "removed", StringComparison.OrdinalIgnoreCase)).ToList();
        var totalDiscount = activeDiscounts.Sum(entry => entry.DiscountAmount);

        DiscountEventsText = $"{entries.Count} events";
        DiscountTotalText = $"£{totalDiscount:F2}";
        DiscountLastEventText = entries.Count > 0
            ? $"Last discount {entries[0].EventAt:dd/MM HH:mm}"
            : "No discounts in this range";

        DiscountEventsColor = entries.Count > 0
            ? Color.FromArgb("#B45309")
            : Color.FromArgb("#64748B");
        DiscountTotalColor = totalDiscount > 0
            ? Color.FromArgb("#DC2626")
            : Color.FromArgb("#64748B");

        foreach (var entry in entries.Take(8))
        {
            DiscountAudits.Add(new DiscountAuditReportRow
            {
                EventAt = entry.EventAt,
                RequestedByName = string.IsNullOrWhiteSpace(entry.RequestedByName) ? "Unknown" : entry.RequestedByName,
                Reason = string.IsNullOrWhiteSpace(entry.Reason) ? "No reason" : entry.Reason,
                AmountDisplay = entry.Action == "removed"
                    ? "Removed"
                    : $"-£{entry.DiscountAmount:F2}",
                AmountColor = entry.Action == "removed"
                    ? Color.FromArgb("#2563EB")
                    : Color.FromArgb("#DC2626"),
                DetailDisplay = BuildDiscountDetail(entry),
                ApprovalDisplay = BuildDiscountApproval(entry)
            });
        }

        OnPropertyChanged(nameof(HasDiscountAudits));
    }

    private static string BuildDiscountDetail(DiscountAuditEntry entry)
    {
        var contextParts = new List<string>();

        if (!string.IsNullOrWhiteSpace(entry.TableNumber))
        {
            contextParts.Add($"Table {entry.TableNumber}");
        }

        if (!string.IsNullOrWhiteSpace(entry.OrderNumber))
        {
            contextParts.Add($"Order {entry.OrderNumber}");
        }

        contextParts.Add(entry.DiscountType == "percent"
            ? $"{entry.DiscountPercent:F1}%"
            : "Fixed");
        contextParts.Add(entry.Action);

        return string.Join(" | ", contextParts);
    }

    private static string BuildDiscountApproval(DiscountAuditEntry entry)
    {
        if (!entry.ApprovalRequired)
        {
            return "No approval required";
        }

        return string.IsNullOrWhiteSpace(entry.ApprovedByName)
            ? "Approval required"
            : $"Approved by {entry.ApprovedByName}";
    }

    private void ApplyVoidCancelledReport(ReportVoidCancelledSnapshot report)
    {
        VoidCancelledOrders.Clear();
        foreach (var order in report.Orders)
        {
            VoidCancelledOrders.Add(order);
        }

        VoidCancelledTotalText = $"{report.Summary.TotalCount} orders";
        VoidCancelledAmountText = $"£{report.Summary.TotalAmount:F2}";
        VoidCancelledVoidedText = $"{report.Summary.VoidedCount} voided · £{report.Summary.VoidedAmount:F2}";
        VoidCancelledCancelledText = $"{report.Summary.CancelledCount} cancelled · £{report.Summary.CancelledAmount:F2}";
        VoidCancelledLastEventText = report.Orders.Count > 0
            ? $"Last event {report.Orders[0].AuditAtDisplay}"
            : "No voided or cancelled orders in this range";

        OnPropertyChanged(nameof(HasVoidCancelledOrders));
    }

    private void ApplyKpiPalette(
        int orderCount,
        decimal gross,
        decimal net,
        decimal vat,
        decimal deliveryCharge,
        decimal average,
        int sendLatencySampleCount,
        double sendLatencyAverageMs,
        int paymentSampleCount,
        double paymentAverageSeconds,
        int draftAbandonmentCount,
        int voidAuditCount)
    {
        SummaryOrdersColor = orderCount <= 0 ? Color.FromArgb("#94A3B8") : Color.FromArgb("#1D4ED8");
        SummaryGrossColor = gross <= 0 ? Color.FromArgb("#94A3B8") : Color.FromArgb("#059669");
        SummaryNetColor = net <= 0 ? Color.FromArgb("#94A3B8") : Color.FromArgb("#0891B2");
        SummaryVatColor = vat <= 0 ? Color.FromArgb("#94A3B8") : Color.FromArgb("#D97706");
        SummaryDeliveryColor = deliveryCharge <= 0 ? Color.FromArgb("#94A3B8") : Color.FromArgb("#0F766E");
        SummaryAverageColor = average <= 0 ? Color.FromArgb("#94A3B8") : Color.FromArgb("#7C3AED");

        OperationalSendLatencyColor = sendLatencySampleCount <= 0
            ? Color.FromArgb("#64748B")
            : sendLatencyAverageMs <= 1500d
                ? Color.FromArgb("#16A34A")
                : sendLatencyAverageMs <= 3500d
                    ? Color.FromArgb("#D97706")
                    : Color.FromArgb("#DC2626");

        OperationalPaymentCompletionColor = paymentSampleCount <= 0
            ? Color.FromArgb("#64748B")
            : paymentAverageSeconds <= 60d
                ? Color.FromArgb("#16A34A")
                : paymentAverageSeconds <= 180d
                    ? Color.FromArgb("#D97706")
                    : Color.FromArgb("#DC2626");

        OperationalDraftAbandonmentColor = draftAbandonmentCount <= 0
            ? Color.FromArgb("#16A34A")
            : draftAbandonmentCount <= 2
                ? Color.FromArgb("#D97706")
                : Color.FromArgb("#DC2626");

        OperationalVoidAuditColor = voidAuditCount <= 0
            ? Color.FromArgb("#16A34A")
            : voidAuditCount <= 2
                ? Color.FromArgb("#D97706")
                : Color.FromArgb("#DC2626");
    }

    private async Task<List<ReportDailyTrendRow>> LoadTrendAsync()
    {
        var sourceFilter = ReportSourceFilter.All;
        var orderTypeFilter = ReportOrderTypeFilter.All;

        if (_reportViewMode == ReportViewMode.Orders)
        {
            sourceFilter = Enum.TryParse<ReportSourceFilter>(SelectedSourceFilter, true, out var sourceValue)
                ? sourceValue
                : ReportSourceFilter.All;

            orderTypeFilter = Enum.TryParse<ReportOrderTypeFilter>(SelectedOrderTypeFilter, true, out var typeValue)
                ? typeValue
                : ReportOrderTypeFilter.All;
        }

        return await _reportService.GetDailyTrendAsync(StartDate, EndDate, sourceFilter, orderTypeFilter);
    }

    private void ApplyTrend(List<ReportDailyTrendRow> rows)
    {
        DailyTrend.Clear();
        foreach (var row in rows)
        {
            DailyTrend.Add(row);
        }

        ConfigureTrendChartAxis();

        if (rows.Count == 0)
        {
            TrendInsightText = "No sales in the selected date range.";
            TrendInsightColor = Color.FromArgb("#64748B");
            return;
        }

        if (rows.Count == 1)
        {
            var point = rows[0];
            var isSingleDay = StartDate.Date == EndDate.Date;
            TrendInsightText = isSingleDay
                ? $"Today: {point.OrderCount} orders · £{point.GrossSales:F2} gross."
                : $"Single period: {point.OrderCount} orders · £{point.GrossSales:F2} gross.";
            TrendInsightColor = Color.FromArgb("#1D4ED8");
            return;
        }

        var first = rows.First();
        var last = rows.Last();
        var baseGross = first.GrossSales <= 0 ? 1m : first.GrossSales;
        var changePct = ((last.GrossSales - first.GrossSales) / baseGross) * 100m;
        var peakPoint = rows.OrderByDescending(row => row.GrossSales).First();
        var isHourly = StartDate.Date == EndDate.Date;

        if (changePct >= 15m)
        {
            TrendInsightText = isHourly
                ? $"Sales rising {changePct:F1}% through the day. Peak hour: {peakPoint.BusinessDate:HH:mm} (£{peakPoint.GrossSales:F2})."
                : $"Momentum up {changePct:F1}% in this range. Peak day: {peakPoint.BusinessDate:dd MMM} (£{peakPoint.GrossSales:F2}).";
            TrendInsightColor = Color.FromArgb("#15803D");
        }
        else if (changePct <= -15m)
        {
            TrendInsightText = isHourly
                ? $"Sales down {Math.Abs(changePct):F1}% through the day. Peak hour was {peakPoint.BusinessDate:HH:mm}."
                : $"Sales down {Math.Abs(changePct):F1}% in this range. Peak day was {peakPoint.BusinessDate:dd MMM}.";
            TrendInsightColor = Color.FromArgb("#B91C1C");
        }
        else
        {
            TrendInsightText = isHourly
                ? $"Hourly trend ({changePct:+0.0;-0.0;0.0}%). Peak hour: {peakPoint.BusinessDate:HH:mm} with {peakPoint.OrderCount} orders."
                : $"Stable trend ({changePct:+0.0;-0.0;0.0}%). Peak day: {peakPoint.BusinessDate:dd MMM} with {peakPoint.OrderCount} orders.";
            TrendInsightColor = Color.FromArgb("#1D4ED8");
        }
    }

    private void ConfigureTrendChartAxis()
    {
        if (SalesTrendXAxis == null)
        {
            return;
        }

        SalesTrendXAxis.IntervalType = StartDate.Date == EndDate.Date
            ? Syncfusion.Maui.Charts.DateTimeIntervalType.Hours
            : Syncfusion.Maui.Charts.DateTimeIntervalType.Days;
    }

    private static object? GetNestedMetric(object? source, string propertyName)
    {
        if (source == null)
        {
            return null;
        }

        return source.GetType().GetProperty(propertyName)?.GetValue(source);
    }

    private static int GetIntProperty(object? source, string propertyName)
    {
        if (source == null)
        {
            return 0;
        }

        var value = source.GetType().GetProperty(propertyName)?.GetValue(source);
        if (value == null)
        {
            return 0;
        }

        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static double GetDoubleProperty(object? source, string propertyName)
    {
        if (source == null)
        {
            return 0d;
        }

        var value = source.GetType().GetProperty(propertyName)?.GetValue(source);
        if (value == null)
        {
            return 0d;
        }

        return Convert.ToDouble(value, CultureInfo.InvariantCulture);
    }

    private static IEnumerable<object> GetEnumerableProperty(object? source, string propertyName)
    {
        if (source == null)
        {
            return Enumerable.Empty<object>();
        }

        var value = source.GetType().GetProperty(propertyName)?.GetValue(source) as IEnumerable;
        return value?.Cast<object>() ?? Enumerable.Empty<object>();
    }

    private static List<OperationalVoidAuditRow> BuildVoidAuditRows(IEnumerable<object> analyticsRows)
    {
        var rows = new List<OperationalVoidAuditRow>();

        foreach (var entry in analyticsRows)
        {
            rows.Add(new OperationalVoidAuditRow
            {
                OrderNumber = GetStringProperty(entry, "OrderNumber"),
                EventAt = GetDateTimeProperty(entry, "EventAt"),
                ActorName = GetStringProperty(entry, "ActorName"),
                Reason = GetStringProperty(entry, "Reason")
            });
        }

        return rows;
    }

    private static string GetStringProperty(object? source, string propertyName)
    {
        if (source == null)
        {
            return string.Empty;
        }

        var value = source.GetType().GetProperty(propertyName)?.GetValue(source);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static DateTime GetDateTimeProperty(object? source, string propertyName)
    {
        if (source == null)
        {
            return DateTime.MinValue;
        }

        var value = source.GetType().GetProperty(propertyName)?.GetValue(source);
        return value == null ? DateTime.MinValue : Convert.ToDateTime(value, CultureInfo.InvariantCulture);
    }

    public sealed class OperationalVoidAuditRow
    {
        public string OrderNumber { get; set; } = string.Empty;
        public DateTime EventAt { get; set; }
        public string ActorName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    public sealed class CashDrawerReportRow
    {
        public DateTime EventAt { get; set; }
        public string RequestedByName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string PrinterName { get; set; } = string.Empty;
        public string StatusDisplay { get; set; } = string.Empty;
        public Color StatusColor { get; set; } = Color.FromArgb("#0F172A");
        public string DetailDisplay { get; set; } = string.Empty;
    }

    public sealed class TillExpenseReportRow
    {
        public DateTime EventAt { get; set; }
        public string CategoryDisplay { get; set; } = string.Empty;
        public string StatusDisplay { get; set; } = string.Empty;
        public string SummaryDisplay { get; set; } = string.Empty;
        public string RecordedByName { get; set; } = string.Empty;
        public string AmountDisplay { get; set; } = string.Empty;
    }

    public sealed class DiscountAuditReportRow
    {
        public DateTime EventAt { get; set; }
        public string RequestedByName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string AmountDisplay { get; set; } = string.Empty;
        public Color AmountColor { get; set; } = Color.FromArgb("#0F172A");
        public string DetailDisplay { get; set; } = string.Empty;
        public string ApprovalDisplay { get; set; } = string.Empty;
    }

    protected override void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
    }

    // ============================================================================
    // Phase 2: Report Generation Service Integration
    // ============================================================================

    /// <summary>
    /// Display ReportSnapshot data in the UI
    /// </summary>
    private void DisplayReportSnapshot(ReportSnapshot report)
    {
        try
        {
            // Update summary metrics
            SummaryOrdersText = report.OrderCount.ToString();
            SummaryGrossText = report.GrossDisplay;
            SummaryNetText = report.NetDisplay;
            SummaryVatText = report.VatDisplay;
            SummaryDeliveryText = "£0.00";
            SummaryAverageText = $"£{report.AverageOrderValue:F2}";
            _summaryCashPaidText = $"£{report.CashTotal:F2}";
            _summaryCardPaidText = $"£{report.CardTotal:F2}";
            _summaryGiftCardPaidText = $"£{report.GiftCardTotal:F2}";
            _summaryCollectedText = $"£{(report.CashTotal + report.CardTotal + report.GiftCardTotal + report.MobilePayTotal):F2}";
            OnPropertyChanged(nameof(SummaryCashPaidText));
            OnPropertyChanged(nameof(SummaryCardPaidText));
            OnPropertyChanged(nameof(SummaryGiftCardPaidText));
            OnPropertyChanged(nameof(SummaryCollectedText));
            ApplyKpiPalette(
                report.OrderCount,
                report.GrossSales,
                report.NetSales,
                report.VatTotal,
                deliveryCharge: 0m,
                report.AverageOrderValue,
                sendLatencySampleCount: 0,
                sendLatencyAverageMs: 0d,
                paymentSampleCount: 0,
                paymentAverageSeconds: 0d,
                draftAbandonmentCount: 0,
                voidAuditCount: report.VoidCount);

            // Clear and display order-level data if available
            Orders.Clear();
            if (report.OrderCount > 0)
            {
                // Note: ReportSnapshot contains aggregated data, display summary
                System.Diagnostics.Debug.WriteLine($" Report Snapshot: {report.PeriodDisplay}");
                System.Diagnostics.Debug.WriteLine($"   Orders: {report.OrderCount}");
                System.Diagnostics.Debug.WriteLine($"   Gross: {report.GrossDisplay}");
                System.Diagnostics.Debug.WriteLine($"   VAT: {report.VatDisplay}");
                System.Diagnostics.Debug.WriteLine($"   Net: {report.NetDisplay}");
            }

            // Display VAT breakdown by rate
            DisplayVatBreakdown(report.VatBreakdowns);

            // Display payment methods
            DisplayPaymentMethods(report.PaymentMethods);

            // Display staff performance
            DisplayStaffPerformance(report.StaffMetrics);

            // Display order type analysis
            DisplayOrderTypeAnalysis(report.OrderTypeAnalysis);

            OnPropertyChanged(nameof(IsTopItemsEmpty));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error displaying report: {ex.Message}");
        }
    }

    /// <summary>
    /// Display VAT breakdown by rate (0%, 20%)
    /// </summary>
    private void DisplayVatBreakdown(List<ReportVatBreakdown> vatBreakdowns)
    {
        try
        {
            var vatSummary = new StringBuilder();
            vatSummary.AppendLine("VAT Breakdown:");
            
            foreach (var vat in vatBreakdowns)
            {
                vatSummary.AppendLine($"  {vat.VatCategoryName}: {vat.VatDisplay} ({vat.ItemCount} items)");
            }

            System.Diagnostics.Debug.WriteLine(vatSummary.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error displaying VAT breakdown: {ex.Message}");
        }
    }

    /// <summary>
    /// Display payment methods breakdown
    /// </summary>
    private void DisplayPaymentMethods(List<ReportPaymentMethod> paymentMethods)
    {
        try
        {
            var summary = new StringBuilder();
            summary.AppendLine("Payment Methods:");
            
            foreach (var method in paymentMethods)
            {
                summary.AppendLine($"  {method.PaymentMethod}: {method.TransactionCount} transactions, {method.AmountDisplay}");
            }

            System.Diagnostics.Debug.WriteLine(summary.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error displaying payment methods: {ex.Message}");
        }
    }

    /// <summary>
    /// Display staff performance metrics
    /// </summary>
    private void DisplayStaffPerformance(List<ReportStaffPerformance> staffPerformance)
    {
        try
        {
            var summary = new StringBuilder();
            summary.AppendLine("Staff Performance:");
            
            foreach (var staff in staffPerformance)
            {
                summary.AppendLine($"  {staff.StaffName}: {staff.OrdersProcessed} orders, {staff.SalesDisplay}");
            }

            System.Diagnostics.Debug.WriteLine(summary.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error displaying staff performance: {ex.Message}");
        }
    }

    /// <summary>
    /// Display order type analysis (dine-in, delivery, pickup, online)
    /// </summary>
    private void DisplayOrderTypeAnalysis(List<ReportOrderTypeAnalysis> orderTypes)
    {
        try
        {
            var summary = new StringBuilder();
            summary.AppendLine("Order Types:");
            
            foreach (var type in orderTypes)
            {
                summary.AppendLine($"  {type.OrderType}: {type.OrderCount} orders, {type.SalesDisplay}");
            }

            System.Diagnostics.Debug.WriteLine(summary.ToString());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error displaying order types: {ex.Message}");
        }
    }

    // ============================================================================
    // NEW: Report History & Comparison - Phase 3
    // ============================================================================

    /// <summary>
    /// Load report from 6 months ago (quick access)
    /// </summary>
    public async Task LoadReportFromSixMonthsAgoAsync()
    {
        if (!CanUseFullReportTools)
        {
            await ShowMotherReportOnlyAlertAsync();
            return;
        }

        try
        {
            IsLoading = true;
            var targetDate = DateTime.Today.AddMonths(-6);
            var snapshot = await _reportHistoryService.GetDailyReportForDateAsync(targetDate);

            if (snapshot != null)
            {
                await LoadHistoricalReportWithComparisonAsync(snapshot.Id);
                return;
            }

            ApplyReportMode(ReportViewMode.Orders);
            _selectedPreset = ReportDatePreset.Custom;
            StartDate = targetDate;
            EndDate = targetDate;
            IsCustomRangeVisible = false;
            UpdatePresetButtonStyles();
            await LoadReportAsync();
            await AppAlertService.ShowAlertAsync(
                "Live Data",
                $"No saved snapshot for {targetDate:dd MMM yyyy}. Showing live orders for that date instead.");
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Error", $"Failed to load historical report: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Load report from 1 year ago (quick access)
    /// </summary>
    public async Task LoadReportFromOneYearAgoAsync()
    {
        if (!CanUseFullReportTools)
        {
            await ShowMotherReportOnlyAlertAsync();
            return;
        }

        try
        {
            IsLoading = true;
            var targetDate = DateTime.Today.AddYears(-1);
            var snapshot = await _reportHistoryService.GetDailyReportForDateAsync(targetDate);

            if (snapshot != null)
            {
                await LoadHistoricalReportWithComparisonAsync(snapshot.Id);
                return;
            }

            ApplyReportMode(ReportViewMode.Orders);
            _selectedPreset = ReportDatePreset.Custom;
            StartDate = targetDate;
            EndDate = targetDate;
            IsCustomRangeVisible = false;
            UpdatePresetButtonStyles();
            await LoadReportAsync();
            await AppAlertService.ShowAlertAsync(
                "Live Data",
                $"No saved snapshot for {targetDate:dd MMM yyyy}. Showing live orders for that date instead.");
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Error", $"Failed to load historical report: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Load all available reports for browsing
    /// </summary>
    public async Task<List<ReportHistorySummary>> GetAvailableReportsAsync(int? monthsBack = null)
    {
        try
        {
            var reports = await _reportHistoryService.GetAvailableReportsAsync(monthsBack ?? 12);
            System.Diagnostics.Debug.WriteLine($" Found {reports.Count} available reports in last {monthsBack ?? 12} months");
            return reports;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error retrieving available reports: {ex.Message}");
            return new List<ReportHistorySummary>();
        }
    }

    /// <summary>
    /// Get all daily reports for a specific month
    /// </summary>
    public async Task<List<ReportHistorySummary>> GetDailyReportsForMonthAsync(int year, int month)
    {
        try
        {
            var reports = await _reportHistoryService.GetDailyReportsForMonthAsync(year, month);
            System.Diagnostics.Debug.WriteLine($" Found {reports.Count} daily reports for {year}-{month:00}");
            return reports;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error retrieving daily reports: {ex.Message}");
            return new List<ReportHistorySummary>();
        }
    }

    /// <summary>
    /// Get all monthly reports for year range (for trend analysis)
    /// </summary>
    public async Task<List<ReportHistorySummary>> GetMonthlyReportsForTrendAsync(int? yearFrom = null, int? yearTo = null)
    {
        try
        {
            var from = yearFrom ?? DateTime.Now.Year - 1;
            var to = yearTo ?? DateTime.Now.Year;
            var reports = await _reportHistoryService.GetMonthlyReportsAsync(from, to);
            System.Diagnostics.Debug.WriteLine($" Found {reports.Count} monthly reports for trend analysis");
            return reports;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error retrieving monthly reports: {ex.Message}");
            return new List<ReportHistorySummary>();
        }
    }

    /// <summary>
    /// Load a specific historical report by ID and calculate comparison
    /// </summary>
    public async Task LoadHistoricalReportWithComparisonAsync(int snapshotId)
    {
        if (!CanUseFullReportTools)
        {
            await ShowMotherReportOnlyAlertAsync();
            return;
        }

        try
        {
            IsLoading = true;
            var report = await _reportHistoryService.GetReportByIdAsync(snapshotId);
            
            if (report == null)
            {
                await AppAlertService.ShowAlertAsync("Not Found", "Report snapshot not found.");
                return;
            }

            await _reportHistoryService.EnrichComparisonsAsync(report);

            // Display the snapshot
            DisplayReportSnapshot(report);

            // Display available comparisons
            await DisplayComparisonAsync(report);

            LastUpdatedText = $"Historical report: {report.StartDate:MMM dd, yyyy} to {report.EndDate:MMM dd, yyyy}";
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Error", $"Failed to load historical report: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Calculate and display comparison with previous period
    /// </summary>
    private async Task DisplayComparisonAsync(ReportSnapshot currentReport)
    {
        try
        {
            if (currentReport.ComparisonVsPrior != null)
            {
                ComparisonPriorText = BuildComparisonText(currentReport.ComparisonVsPrior);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Info: No prior-period comparison data available");
                ComparisonPriorText = "No prior-period comparison available.";
            }

            if (currentReport.ComparisonVsYearAgo != null)
            {
                ComparisonYearAgoText = BuildComparisonText(currentReport.ComparisonVsYearAgo);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Info: No year-over-year comparison data available");
                ComparisonYearAgoText = "No year-over-year comparison available.";
            }

            IsComparisonPanelVisible = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error calculating comparison: {ex.Message}");
        }
    }

    /// <summary>
    /// Display comparison metrics (DoD, WoW, MoM, YoY)
    /// </summary>
    private void DisplayComparison(ReportComparison comparison)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine(BuildComparisonText(comparison));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error displaying comparison: {ex.Message}");
        }
    }

    private static string BuildComparisonText(ReportComparison comparison)
    {
        var report = new StringBuilder();
        report.AppendLine($"{comparison.ComparisonLabel}");
        report.AppendLine($"Sales {comparison.SalesChangeDisplay}");
        report.AppendLine($"Orders {comparison.OrderCountChangeDisplay}");
        report.AppendLine($"Margin {(comparison.MarginChange > 0 ? "+" : "")}{comparison.MarginChange:F1}%");
        report.AppendLine($"VAT {(comparison.VatChange > 0 ? "+" : "")}{comparison.VatChange:F1}%");
        report.AppendLine(comparison.InsightText);

        if (comparison.Anomalies.Count > 0)
        {
            report.AppendLine("Anomalies:");
            foreach (var anomaly in comparison.Anomalies)
            {
                report.AppendLine($"- {anomaly}");
            }
        }

        return report.ToString().TrimEnd();
    }

    /// <summary>
    /// Helper: Get daily reports for calendar picker
    /// </summary>
    public async Task<List<DateTime>> GetAvailableDatesAsync(int year, int month)
    {
        try
        {
            var reports = await GetDailyReportsForMonthAsync(year, month);
            return reports.Select(r => r.StartDate.Date).Distinct().ToList();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($" Error getting available dates: {ex.Message}");
            return new List<DateTime>();
        }
    }

    /// <summary>
    /// Load the latest report snapshots into the history browser
    /// </summary>
    public async Task LoadHistoricalReportsAsync(int? monthsBack = null, bool updateStatusText = true)
    {
        if (!CanUseFullReportTools)
        {
            await MainThread.InvokeOnMainThreadAsync(() => HistoricalReports.Clear());
            return;
        }

        try
        {
            IsLoading = true;
            var reports = await _reportHistoryService.GetAvailableReportsAsync(monthsBack ?? 12);
            var lastUpdated = updateStatusText
                ? $"Loaded {reports.Count} saved snapshots · updated {DateTime.Now:HH:mm:ss}"
                : null;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                HistoricalReports.Clear();
                foreach (var report in reports)
                {
                    HistoricalReports.Add(report);
                }

                System.Diagnostics.Debug.WriteLine($" Loaded {HistoricalReports.Count} historical reports for browsing");
                if (lastUpdated != null)
                {
                    LastUpdatedText = lastUpdated;
                }
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await AppAlertService.ShowAlertAsync("Error", $"Failed to load historical reports: {ex.Message}");
            });
        }
        finally
        {
            MainThread.BeginInvokeOnMainThread(() => IsLoading = false);
        }
    }

    private async void OnLoadHistoricalReportsClicked(object sender, EventArgs e)
    {
        if (!CanUseFullReportTools)
        {
            await ShowMotherReportOnlyAlertAsync();
            return;
        }

        await LoadHistoricalReportsAsync();
    }

    private async void OnHistoricalReportSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ReportHistorySummary report)
            return;

        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }

        await LoadHistoricalReportWithComparisonAsync(report.Id);
    }

    private async void OnLoadSixMonthsAgoClicked(object sender, EventArgs e)
    {
        await LoadReportFromSixMonthsAgoAsync();
    }

    private async void OnLoadOneYearAgoClicked(object sender, EventArgs e)
    {
        await LoadReportFromOneYearAgoAsync();
    }

    private static Task ShowMotherReportOnlyAlertAsync()
    {
        return AppAlertService.ShowAlertAsync(
            "Mother Terminal Only",
            "This report action is available on the mother terminal. Child terminals can view limited live reports from the shared database.");
    }

    private async Task RefreshOrderWebUploadStateAsync()
    {
        if (!CanUseFullReportTools || !TerminalRoleService.CanRunMotherJobs)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                IsOrderWebUploadBannerVisible = false;
            });
            return;
        }

        OrderWebDailyUploadStatus status;
        try
        {
            status = await _orderWebDailyReportSyncService.GetAutoUploadStatusAsync();
        }
        catch
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                IsOrderWebUploadBannerVisible = false;
            });
            return;
        }

        string bannerText;
        try
        {
            bannerText = status.FormatVatFinalBannerText();
        }
        catch
        {
            bannerText = "VAT final at 3:00 AM · status unavailable.";
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            OrderWebUploadBannerText = bannerText;
            IsOrderWebUploadBannerVisible = true;
        });
    }
}

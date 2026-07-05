using System.Collections.ObjectModel;
using System.Globalization;
using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;
using CommunityToolkit.Maui.Storage;
using POS_in_NET.Models;
using POS_in_NET.Services;
using POS_in_NET.Views;

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
    private string _summaryAverageText = "£0.00";
    private bool _isCustomRangeDirty;
    private ReportViewMode _reportViewMode = ReportViewMode.Orders;
    private int _topSellRangeDays = 30;
    private TopSellSection _selectedTopSellSection = TopSellSection.Food;
    private bool _isTopSellCustomRangeVisible;
    private bool _isTopSellCustomRangeDirty;
    private bool _isCalendarPopupVisible;
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

    private enum ReportViewMode
    {
        Orders,
        TopSellItems,
        CashDrawer,
        DiscountAudit,
        StaffHours,
        VoidCancelled,
        HistoricalReports
    }

    private enum CalendarTarget
    {
        Start,
        End
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
        _orderWebDailyReportSyncService = ServiceHelper.GetService<OrderWebDailyReportSyncService>()
            ?? new OrderWebDailyReportSyncService(
                new DatabaseService(),
                ServiceHelper.GetService<ZReportService>()
                    ?? new ZReportService(
                        new DatabaseService(),
                        _reportService,
                        _tillExpenseService,
                        _discountAuditService,
                        _businessSettingsService),
                ServiceHelper.GetService<TimeClockService>() ?? new TimeClockService(new DatabaseService()));
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

        if (!await _permissionService.HasPermissionAsync(PermissionKeys.ReportView))
        {
            await AppAlertService.ShowAlertAsync("Access Denied", "Only Admin can access Reports.");
            await Shell.Current.GoToAsync($"//{_roleAccessService.ResolveDashboardRoute(_authService.CurrentUser?.Role)}");
            return;
        }

        if (!_hasLoaded)
        {
            _hasLoaded = true;
            await LoadReportAsync();
            if (CanUseFullReportTools)
            {
                await LoadHistoricalReportsAsync(updateStatusText: false);
            }
        }

        await RefreshOrderWebUploadStateAsync();
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

        AppDataRefreshService.RefreshRequested += OnRefreshRequested;
        AppDataRefreshService.DataChanged += OnAppDataChanged;
        _isSubscribedToRefreshEvents = true;
    }

    private void UnsubscribeFromRefreshEvents()
    {
        if (!_isSubscribedToRefreshEvents)
        {
            return;
        }

        AppDataRefreshService.RefreshRequested -= OnRefreshRequested;
        AppDataRefreshService.DataChanged -= OnAppDataChanged;
        _isSubscribedToRefreshEvents = false;
    }

    private async void OnRefreshRequested(object? sender, EventArgs e)
    {
        await RefreshCurrentReportAsync();
    }

    private async void OnAppDataChanged(object? sender, AppDataChangedEventArgs e)
    {
        if (e.IsFromCurrentTerminal || (e.Kind != AppDataChangeKind.Orders && e.Kind != AppDataChangeKind.All))
        {
            return;
        }

        await RefreshCurrentReportAsync();
    }

    private async Task RefreshCurrentReportAsync()
    {
        await LoadReportAsync();
        if (CanUseFullReportTools)
        {
            await LoadHistoricalReportsAsync(updateStatusText: false);
        }

        await RefreshOrderWebUploadStateAsync();
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
                UpdatePresetButtonStyles();
                await LoadHistoricalReportsAsync();
                return;
            }

            ApplyReportMode(ReportViewMode.Orders);

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

    private void UpdateTopSellRangeButtonStyles()
    {
        ApplyPresetStyle(TopSellThirtyDaysButton, TopSellRangeDays == 30);
        ApplyPresetStyle(TopSellSixtyDaysButton, TopSellRangeDays == 60);
        ApplyPresetStyle(TopSellNinetyDaysButton, TopSellRangeDays == 90);
        ApplyPresetStyle(TopSellCustomRangeButton, IsTopSellCustomRangeVisible);
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
            await Shell.Current.GoToAsync($"reportdetails?orderDbId={selectedOrder.OrderDbId}");
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

        var orderLabel = string.IsNullOrWhiteSpace(order.OrderNumber) ? order.OrderId : order.OrderNumber;
        var confirm = await DisplayAlert(
            "Void Order",
            $"Permanently remove order {orderLabel}?\n\nThis is for demo/test orders only. It will delete the order, items, payments, events, print queue entries, and related local records.",
            "Void / Delete",
            "Cancel");

        if (!confirm)
        {
            return;
        }

        try
        {
            button.IsEnabled = false;
            var deleted = await _reportService.HardDeleteOrderAsync(order.OrderDbId);

            if (!deleted)
            {
                await AppAlertService.ShowAlertAsync("Not Found", $"Order {orderLabel} was not found.");
                return;
            }

            await AppAlertService.ShowAlertAsync("Order Removed", $"Order {orderLabel} has been permanently deleted.");
            await LoadReportAsync();
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Void Failed", ex.Message);
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
        ApplyPresetStyle(HistoricalReportsButton, _reportViewMode == ReportViewMode.HistoricalReports);
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

        IsLoading = true;

        try
        {
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
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await AppAlertService.ShowAlertAsync("Report Error", ex.Message);
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

        SummaryOrdersText = report.Summary.OrderCount.ToString(CultureInfo.InvariantCulture);
        SummaryGrossText = $"£{report.Summary.GrossSales:F2}";
        SummaryNetText = $"£{report.Summary.NetSales:F2}";
        SummaryVatText = $"£{report.Summary.VatAmount:F2}";
        SummaryDeliveryText = $"£{report.Summary.DeliveryChargeTotal:F2}";
        SummaryAverageText = $"£{report.Summary.AverageOrderValue:F2}";

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
    /// Display VAT breakdown by rate (0%, 5%, 20%)
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
        var uploadDate = ResolveOrderWebUploadDate();
        var alreadyUploaded = uploadDate.HasValue
            && await _orderWebDailyReportSyncService.IsAlreadyUploadedAsync(uploadDate.Value);
        var pendingDate = _orderWebDailyReportSyncService.GetPendingUploadDate();
        var bannerText = uploadDate.HasValue
            ? $"In-restaurant totals for {uploadDate.Value:ddd dd MMM yyyy} are ready. Upload once to OrderWeb admin when you have closed the day."
            : string.Empty;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            OnPropertyChanged(nameof(CanUploadOrderWebReport));

            if (!CanUploadOrderWebReport || uploadDate == null)
            {
                IsOrderWebUploadBannerVisible = false;
                return;
            }

            if (alreadyUploaded)
            {
                if (pendingDate == uploadDate.Value)
                {
                    _orderWebDailyReportSyncService.ClearPendingUpload();
                }

                IsOrderWebUploadBannerVisible = false;
                return;
            }

            OrderWebUploadBannerText = bannerText;
            IsOrderWebUploadBannerVisible = true;
        });
    }

    private DateTime? ResolveOrderWebUploadDate()
    {
        var pending = _orderWebDailyReportSyncService.GetPendingUploadDate();
        if (pending.HasValue)
        {
            return pending.Value;
        }

        if (_startDate.Date == _endDate.Date)
        {
            return _startDate.Date;
        }

        return DateTime.Today.AddDays(-1);
    }

    private async void OnUploadOrderWebReportClicked(object sender, EventArgs e)
    {
        if (!CanUploadOrderWebReport)
        {
            await ShowMotherReportOnlyAlertAsync();
            return;
        }

        var uploadDate = ResolveOrderWebUploadDate();
        if (uploadDate == null)
        {
            await AppAlertService.ShowAlertAsync(
                "No Report Date",
                "Select a single day on the report page, or wait for the nightly scheduler to mark yesterday as ready.");
            return;
        }

        if (await _orderWebDailyReportSyncService.IsAlreadyUploadedAsync(uploadDate.Value))
        {
            await AppAlertService.ShowAlertAsync(
                "Already Uploaded",
                $"Daily report for {uploadDate.Value:dd MMM yyyy} was already sent to OrderWeb.");
            await RefreshOrderWebUploadStateAsync();
            return;
        }

        var uploadConfirmDialog = new ModernConfirmDialog();
        uploadConfirmDialog.SetConfirm(
            "Upload to OrderWeb",
            $"Send in-restaurant totals for {uploadDate.Value:ddd dd MMM yyyy} to OrderWeb.net?",
            "Upload",
            "Cancel",
            "OK",
            "#0F766E");

        var confirm = await uploadConfirmDialog.ShowAsync();

        if (!confirm)
        {
            return;
        }

        try
        {
            if (sender is Button button)
            {
                button.IsEnabled = false;
                button.Text = "Uploading...";
            }

            var result = await _orderWebDailyReportSyncService.UploadManualAsync(uploadDate.Value);
            await AppAlertService.ShowAlertAsync(
                result.Success ? "Upload Complete" : "Upload Failed",
                result.Message);
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Upload Failed", ex.Message);
        }
        finally
        {
            if (sender is Button button)
            {
                button.IsEnabled = CanUploadOrderWebReport;
                var originalText = (string?)button.CommandParameter ?? "Upload to OrderWeb";
                button.Text = originalText;
            }

            await RefreshOrderWebUploadStateAsync();
        }
    }
}

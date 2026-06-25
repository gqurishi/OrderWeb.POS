using System.ComponentModel;
using System.Runtime.CompilerServices;
using POS_in_NET.Services;

namespace POS_in_NET.Pages;

[QueryProperty(nameof(OrderDbId), "orderDbId")]
public partial class ReportOrderDetailsPage : ContentPage
{
    private readonly DailyReportService _reportService;
    private bool _isLoaded;
    private bool _isLoading;
    private int _orderDbId;
    private ReportOrderDetail? _order;
    private string _headerLine = "Loading order details...";

    public int OrderDbId
    {
        get => _orderDbId;
        set
        {
            if (_orderDbId != value)
            {
                _orderDbId = value;
                OnPropertyChanged();
                _ = TryLoadAsync();
            }
        }
    }

    public ReportOrderDetail? Order
    {
        get => _order;
        set
        {
            if (_order != value)
            {
                _order = value;
                OnPropertyChanged();
            }
        }
    }

    public string HeaderLine
    {
        get => _headerLine;
        set
        {
            if (_headerLine != value)
            {
                _headerLine = value;
                OnPropertyChanged();
            }
        }
    }

    public ReportOrderDetailsPage()
    {
        InitializeComponent();
        BindingContext = this;
        _reportService = ServiceHelper.GetService<DailyReportService>() ?? new DailyReportService(new DatabaseService());
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await TryLoadAsync();
    }

    private async Task TryLoadAsync()
    {
        if (_isLoading || _isLoaded || _orderDbId <= 0)
        {
            return;
        }

        if (!TerminalRoleService.CanViewFullReports)
        {
            _isLoaded = true;
            HeaderLine = "Mother terminal only";
            await AppAlertService.ShowAlertAsync(
                "Mother Terminal Only",
                "Order drill-down reports are available on the mother terminal. Child terminals can view limited live reports from the shared database.");
            await Shell.Current.GoToAsync("..");
            return;
        }

        _isLoading = true;
        try
        {
            HeaderLine = $"Order #{_orderDbId}";
            Order = await _reportService.GetOrderDetailAsync(_orderDbId);

            if (Order == null)
            {
                HeaderLine = "Order not found";
                await AppAlertService.ShowAlertAsync("Not Found", "The selected order could not be loaded.");
                return;
            }

            HeaderLine = $"{Order.OrderNumber} · {Order.CreatedAtDisplay} · {Order.SourceChannel}/{Order.OrderType}";
            _isLoaded = true;
        }
        catch (Exception ex)
        {
            await AppAlertService.ShowAlertAsync("Report Error", ex.Message);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async void OnCloseClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    protected override void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
    }
}

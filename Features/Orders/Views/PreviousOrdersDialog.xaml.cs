using POS_in_NET.Helpers;
using POS_in_NET.Models;

namespace POS_in_NET.Views;

public partial class PreviousOrdersDialog : ContentView
{
    private TaskCompletionSource<bool>? _taskCompletionSource;
    private Grid? _parentGrid;

    public PreviousOrdersDialog()
    {
        InitializeComponent();
        TabletLayoutHelper.AttachDialog(this, DialogCard, 760, 760);
    }

    public void SetCustomer(string customerName, string customerPhone)
    {
        CustomerLabel.Text = string.IsNullOrWhiteSpace(customerPhone)
            ? customerName
            : $"{customerName} · {customerPhone}";
    }

    public void SetOrders(IReadOnlyList<CustomerPreviousOrder> orders)
    {
        OrdersList.ItemsSource = orders;
        OrdersList.IsVisible = orders.Count > 0;
        EmptyState.IsVisible = orders.Count == 0;
    }

    public async Task ShowAsync()
    {
        _taskCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (Application.Current?.MainPage != null)
        {
            var pageContent = GetPageContent(Application.Current.MainPage);
            if (pageContent is Grid mainGrid)
            {
                _parentGrid = mainGrid;
                Grid.SetRowSpan(this, mainGrid.RowDefinitions.Count > 0 ? mainGrid.RowDefinitions.Count : 1);
                Grid.SetColumnSpan(this, mainGrid.ColumnDefinitions.Count > 0 ? mainGrid.ColumnDefinitions.Count : 1);
                Grid.SetRow(this, 0);
                Grid.SetColumn(this, 0);
                mainGrid.Children.Add(this);
            }
        }

        await _taskCompletionSource.Task;
    }

    private static View? GetPageContent(Page page)
    {
        if (page is Shell shell && shell.CurrentPage is ContentPage currentPage)
        {
            return currentPage.Content;
        }

        return page is ContentPage contentPage ? contentPage.Content : null;
    }

    private void OnCloseClicked(object sender, EventArgs e)
    {
        if (_parentGrid != null)
        {
            _parentGrid.Children.Remove(this);
        }

        _taskCompletionSource?.TrySetResult(true);
    }
}

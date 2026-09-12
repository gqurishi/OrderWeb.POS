namespace OrderWeb.SharedUI.Views;

public partial class ReservationView : ContentView
{
    public ReservationView()
    {
        InitializeComponent();
    }

    public Entry SearchBox => SearchEntry;
    public CollectionView Calendar => CalendarCollectionView;
    public Grid LayoutGrid => MainLayoutGrid;

    public event EventHandler? UpdateRequested;
    public event EventHandler? NewReservationRequested;
    public event EventHandler? TodayRequested;
    public event EventHandler? PreviousMonthRequested;
    public event EventHandler? NextMonthRequested;
    public event EventHandler? SearchBoxFocused;
    public event EventHandler<TextChangedEventArgs>? SearchTextChanged;
    public event EventHandler<SelectionChangedEventArgs>? CalendarDaySelected;
    public event EventHandler<ReservationRow>? ReservationSelected;
    public event EventHandler<ReservationRow>? ShowRequested;
    public event EventHandler<ReservationRow>? DetailsRequested;
    public event EventHandler<ReservationRow>? NoShowRequested;
    public event EventHandler<ReservationRow>? CancelRequested;
    public event EventHandler? DetailsClosed;

    public void ClearSearch() => SearchEntry.Text = string.Empty;
    public void FocusSearch() => SearchEntry.Focus();
    public void ClearCalendarSelection() => CalendarCollectionView.SelectedItem = null;

    public void ApplyResponsiveCalendarWidth(double pageWidth)
    {
        if (pageWidth <= 0 || MainLayoutGrid.ColumnDefinitions.Count < 2)
        {
            return;
        }

        MainLayoutGrid.ColumnDefinitions[0].Width = pageWidth switch
        {
            < 1000 => new GridLength(248),
            < 1250 => new GridLength(312),
            _ => new GridLength(348)
        };
    }

    public void FitCalendarHeight(int cellCount) =>
        CalendarCollectionView.HeightRequest = ReservationCalendarDay.HeightForCellCount(cellCount);

    private void OnUpdateClicked(object? sender, EventArgs e) => UpdateRequested?.Invoke(this, EventArgs.Empty);
    private void OnNewReservationClicked(object? sender, EventArgs e) => NewReservationRequested?.Invoke(this, EventArgs.Empty);
    private void OnTodayClicked(object? sender, EventArgs e) => TodayRequested?.Invoke(this, EventArgs.Empty);
    private void OnPreviousMonthClicked(object? sender, EventArgs e) => PreviousMonthRequested?.Invoke(this, EventArgs.Empty);
    private void OnNextMonthClicked(object? sender, EventArgs e) => NextMonthRequested?.Invoke(this, EventArgs.Empty);
    private void OnSearchBoxTapped(object? sender, TappedEventArgs e) => SearchBoxFocused?.Invoke(this, EventArgs.Empty);
    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e) => SearchTextChanged?.Invoke(this, e);
    private void OnCalendarDaySelected(object? sender, SelectionChangedEventArgs e) => CalendarDaySelected?.Invoke(this, e);
    private void OnCloseDetailsClicked(object? sender, EventArgs e) => DetailsClosed?.Invoke(this, EventArgs.Empty);

    private void OnReservationRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject { BindingContext: ReservationRow row })
        {
            ReservationSelected?.Invoke(this, row);
        }
    }

    private void OnReservationShowClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject { BindingContext: ReservationRow row })
        {
            ShowRequested?.Invoke(this, row);
        }
    }

    private void OnReservationDetailsClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject { BindingContext: ReservationRow row })
        {
            DetailsRequested?.Invoke(this, row);
        }
    }

    private void OnReservationNoShowClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject { BindingContext: ReservationRow row })
        {
            NoShowRequested?.Invoke(this, row);
        }
    }

    private void OnReservationCancelClicked(object? sender, EventArgs e)
    {
        if (sender is BindableObject { BindingContext: ReservationRow row })
        {
            CancelRequested?.Invoke(this, row);
        }
    }
}

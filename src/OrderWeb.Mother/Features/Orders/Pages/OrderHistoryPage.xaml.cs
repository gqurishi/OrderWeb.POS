using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using OrderWeb.Contracts.Orders;
using OrderWeb.Contracts.Services;
using OrderWeb.SharedUI.Views;
using POS_in_NET.Models;
using POS_in_NET.Services;
using Syncfusion.Maui.Calendar;
using System.Threading;

namespace POS_in_NET.Pages
{
    public partial class OrderHistoryPage : ContentPage
    {
        private readonly MotherOrderHistoryService _historyService;
        private readonly IOrderSearchService _orderSearchService;
        private readonly OrderHistoryView _historyView = new();
        private readonly SemaphoreSlim _loadGate = new(1, 1);
        private CancellationTokenSource? _loadCts;
        private bool _subscribedToChanges;

        private DateOnly _selectedDate;
        private OpenOrderChannelKind _selectedChannel = OpenOrderChannelKind.All;
        private string _searchQuery = string.Empty;
        private int _pageNumber = 1;
        private bool _hasNextPage;

        public OrderHistoryPage() : this(false)
        {
        }

        protected OrderHistoryPage(bool openWebTab)
        {
            InitializeComponent();

            _historyService = ServiceHelper.GetService<MotherOrderHistoryService>()
                ?? new MotherOrderHistoryService(
                    ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService(),
                    ServiceHelper.GetService<CustomerDataService>() ?? new CustomerDataService(),
                    ServiceHelper.GetService<CloudOrderService>());
            _orderSearchService = ServiceHelper.GetService<IOrderSearchService>()
                ?? new MotherOrderSearchService(
                    ServiceHelper.GetService<DatabaseService>() ?? new DatabaseService(),
                    ServiceHelper.GetService<CustomerDataService>() ?? new CustomerDataService());

            _selectedDate = DateOnly.FromDateTime(DateTime.Today);
            if (openWebTab)
            {
                _selectedChannel = OpenOrderChannelKind.All;
            }

            TopBar.SetPageTitle("Order History");
            HistoryHost.Content = _historyView;

            _historyView.DateFilterRequested += OnDateFilterRequested;
            _historyView.ChannelChanged += OnChannelChanged;
            _historyView.SearchRequested += OnSearchRequested;
            _historyView.SearchOverlayOpened += OnSearchOverlayOpened;
            _historyView.HistoryItemSelected += OnHistoryItemSelected;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            if (!_subscribedToChanges)
            {
                AppDataRefreshService.DataChanged += OnAppDataChanged;
                _subscribedToChanges = true;
            }

            _ = LoadOrdersSafeAsync();
        }

        protected override void OnDisappearing()
        {
            _loadCts?.Cancel();
            if (_subscribedToChanges)
            {
                AppDataRefreshService.DataChanged -= OnAppDataChanged;
                _subscribedToChanges = false;
            }

            base.OnDisappearing();
        }

        private void OnAppDataChanged(object? sender, AppDataChangedEventArgs e)
        {
            if (!e.HasKind(AppDataChangeKind.Orders))
            {
                return;
            }

            _pageNumber = 1;
            _ = LoadOrdersSafeAsync();
        }

        private void OnChannelChanged(object? sender, OpenOrderChannelKind channel)
        {
            _selectedChannel = channel;
            _pageNumber = 1;
            _ = LoadOrdersSafeAsync();
        }

        private async void OnDateFilterRequested(object? sender, DateOnly date)
        {
            await ShowCalendarAsync(date);
        }

        private async void OnSearchOverlayOpened(object? sender, EventArgs e)
        {
            var result = await _orderSearchService.SearchOrdersAsync(new OrderSearchRequestDto(_searchQuery, _selectedDate));
            if (result.IsSuccess && result.Value != null)
            {
                _historyView.ApplySearch(result.Value);
            }
        }

        private void OnSearchRequested(object? sender, string query)
        {
            _searchQuery = query?.Trim() ?? string.Empty;
            _pageNumber = 1;
            _ = LoadOrdersSafeAsync();
        }

        private async void OnHistoryItemSelected(object? sender, OrderHistoryItemDto item)
        {
            if (_historyService.TryResolveCloudOrder(item.OrderId, out var cloudOrder) && cloudOrder != null)
            {
                await Navigation.PushModalAsync(new OrderDetailsModal(cloudOrder));
                return;
            }

            if (_historyService.TryResolveDatabaseId(item.OrderId, out var databaseId))
            {
                await Navigation.PushModalAsync(new OrderDetailsModal(databaseId));
            }
        }

        private async Task LoadOrdersSafeAsync()
        {
            var nextCts = new CancellationTokenSource();
            var previousCts = Interlocked.Exchange(ref _loadCts, nextCts);
            previousCts?.Cancel();

            try
            {
                await _loadGate.WaitAsync(nextCts.Token);
                try
                {
                    await LoadOrdersAsync(nextCts.Token);
                }
                finally
                {
                    _loadGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Order history load failed: {ex.Message}");
                AppDiagnostics.LogFatal("Order History load", ex);
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    await AppAlertService.ShowAlertAsync(
                        "Order History",
                        "Order history could not be loaded. Please try again. If it continues, check the database connection.");
                });
            }
            finally
            {
                if (ReferenceEquals(Interlocked.CompareExchange(ref _loadCts, null, nextCts), nextCts))
                {
                    nextCts.Dispose();
                }
            }
        }

        private async Task LoadOrdersAsync(CancellationToken cancellationToken)
        {
            _historyView.Apply(new OrderHistoryPageDto(
                _selectedDate,
                _selectedChannel,
                _searchQuery,
                Array.Empty<OrderHistoryItemDto>(),
                Array.Empty<OrderHistoryItemDto>(),
                _pageNumber - 1,
                _pageNumber,
                true,
                new OrderWeb.Contracts.Customers.CustomerSyncStatusDto(true, false, DateTimeOffset.UtcNow, "Live"),
                IsLoading: true,
                LoadingMessage: "Loading history…"));

            var result = await _historyService.GetHistoryAsync(
                _selectedDate,
                _selectedChannel,
                string.IsNullOrWhiteSpace(_searchQuery) ? null : _searchQuery,
                _pageNumber - 1,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (result.IsSuccess && result.Value != null)
                {
                    _hasNextPage = result.Value.PageCount > _pageNumber;
                    _historyView.Apply(result.Value);
                    UpdatePagination();
                    return;
                }

                _historyView.Apply(new OrderHistoryPageDto(
                    _selectedDate,
                    _selectedChannel,
                    _searchQuery,
                    Array.Empty<OrderHistoryItemDto>(),
                    Array.Empty<OrderHistoryItemDto>(),
                    _pageNumber - 1,
                    _pageNumber,
                    true,
                    new OrderWeb.Contracts.Customers.CustomerSyncStatusDto(true, true, null, result.Error?.Message ?? "Failed"),
                    StatusBanner: result.Error?.Message,
                    StatusBannerTone: "error"));
                UpdatePagination();
            });
        }

        private void UpdatePagination()
        {
            PageNumberLabel.Text = $"Page {_pageNumber}";
            PreviousPageButton.IsEnabled = _pageNumber > 1;
            PreviousPageButton.Opacity = PreviousPageButton.IsEnabled ? 1 : 0.45;
            NextPageButton.IsEnabled = _hasNextPage;
            NextPageButton.Opacity = _hasNextPage ? 1 : 0.45;
        }

        private void ResetPageAndLoad()
        {
            _pageNumber = 1;
            _ = LoadOrdersSafeAsync();
        }

        private async Task ShowCalendarAsync(DateOnly currentDate)
        {
            var modal = new ContentPage
            {
                BackgroundColor = Color.FromArgb("#80000000")
            };

            var calendar = new SfCalendar
            {
                SelectedDate = currentDate.ToDateTime(TimeOnly.MinValue),
                MinimumDate = new DateTime(2020, 1, 1),
                MaximumDate = DateTime.Today,
                SelectionMode = CalendarSelectionMode.Single,
                HeightRequest = 380,
                Background = Colors.White,
                HeaderView = new CalendarHeaderView
                {
                    Background = Color.FromArgb("#10B981"),
                    TextStyle = new CalendarTextStyle
                    {
                        TextColor = Colors.White,
                        FontSize = 18,
                        FontAttributes = FontAttributes.Bold
                    }
                },
                MonthView = new CalendarMonthView
                {
                    Background = Colors.White,
                    HeaderView = new CalendarMonthHeaderView
                    {
                        Background = Color.FromArgb("#F3F4F6"),
                        TextStyle = new CalendarTextStyle
                        {
                            TextColor = Color.FromArgb("#6B7280"),
                            FontSize = 14,
                            FontAttributes = FontAttributes.Bold
                        }
                    },
                    TextStyle = new CalendarTextStyle
                    {
                        TextColor = Color.FromArgb("#1F2937"),
                        FontSize = 15
                    },
                    TodayTextStyle = new CalendarTextStyle
                    {
                        TextColor = Color.FromArgb("#10B981"),
                        FontSize = 15,
                        FontAttributes = FontAttributes.Bold
                    },
                    TrailingLeadingDatesTextStyle = new CalendarTextStyle
                    {
                        TextColor = Color.FromArgb("#D1D5DB"),
                        FontSize = 14
                    },
                    TodayBackground = Color.FromArgb("#D1FAE5")
                },
                SelectionBackground = Color.FromArgb("#10B981")
            };

            var frame = new Frame
            {
                BackgroundColor = Colors.White,
                Padding = 0,
                CornerRadius = 16,
                HasShadow = true,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                WidthRequest = 420
            };

            var mainLayout = new VerticalStackLayout { Spacing = 0 };
            var headerLayout = new VerticalStackLayout
            {
                BackgroundColor = Color.FromArgb("#10B981"),
                Padding = new Thickness(24, 20),
                Spacing = 4,
                Children =
                {
                    new Label
                    {
                        Text = "Select Date",
                        FontSize = 22,
                        FontAttributes = FontAttributes.Bold,
                        TextColor = Colors.White,
                        HorizontalOptions = LayoutOptions.Start
                    },
                    new Label
                    {
                        Text = "Choose a date to view orders",
                        FontSize = 14,
                        TextColor = Color.FromArgb("#D1FAE5"),
                        HorizontalOptions = LayoutOptions.Start
                    }
                }
            };

            var buttonLayout = new Grid
            {
                Padding = new Thickness(24, 16, 24, 24),
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Star)
                },
                ColumnSpacing = 12
            };

            var cancelBorder = CreateDialogButton("Cancel", Colors.White, Color.FromArgb("#6B7280"), Color.FromArgb("#D1D5DB"));
            cancelBorder.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () => await Navigation.PopModalAsync())
            });

            var okBorder = CreateDialogButton("Apply", Color.FromArgb("#10B981"), Colors.White, Colors.Transparent);
            okBorder.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () =>
                {
                    if (calendar.SelectedDate.HasValue)
                    {
                        _selectedDate = DateOnly.FromDateTime(calendar.SelectedDate.Value);
                        ResetPageAndLoad();
                    }

                    await Navigation.PopModalAsync();
                })
            });

            buttonLayout.Add(cancelBorder, 0);
            buttonLayout.Add(okBorder, 1);
            mainLayout.Children.Add(headerLayout);
            mainLayout.Children.Add(new VerticalStackLayout { Padding = new Thickness(16), Children = { calendar } });
            mainLayout.Children.Add(buttonLayout);
            frame.Content = mainLayout;
            modal.Content = frame;
            await Navigation.PushModalAsync(modal);
        }

        private static Border CreateDialogButton(string text, Color background, Color foreground, Color stroke)
        {
            var border = new Border
            {
                BackgroundColor = background,
                StrokeThickness = stroke == Colors.Transparent ? 0 : 2,
                Stroke = stroke,
                Padding = new Thickness(0, 14),
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Content = new Label
                {
                    Text = text,
                    TextColor = foreground,
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };
            return border;
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            var authService = ServiceHelper.GetService<AuthenticationService>() ?? AuthenticationService.Instance;
            var roleAccess = ServiceHelper.GetService<RoleAccessService>() ?? new RoleAccessService();
            await NavigationCoordinator.Shared.NavigateShellAsync(
                roleAccess.ResolveDashboardRoute(authService.CurrentUser?.Role),
                source: sender as VisualElement);
        }

        private void OnPreviousPageClicked(object sender, EventArgs e)
        {
            if (_pageNumber <= 1)
            {
                return;
            }

            _pageNumber--;
            _ = LoadOrdersSafeAsync();
        }

        private void OnNextPageClicked(object sender, EventArgs e)
        {
            if (!_hasNextPage)
            {
                return;
            }

            _pageNumber++;
            _ = LoadOrdersSafeAsync();
        }
    }
}

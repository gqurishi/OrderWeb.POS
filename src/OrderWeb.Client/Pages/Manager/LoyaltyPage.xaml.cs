namespace OrderWeb.Client.Pages.Manager;

using OrderWeb.Client.Pages.Orders;
using OrderWeb.Client.Pages.Pos;
using OrderWeb.Client.Services;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Features;
using OrderWeb.SharedUI.Views;

public partial class LoyaltyPage : ContentPage
{
    private readonly ClientCacheService _cache = new();
    private readonly ClientOfflinePolicy _offlinePolicy;
    private readonly MotherLoyaltyClient _loyalty;
    private ClientLoyaltyCustomerDto? _currentCustomer;
    private string? _currentLookup;
    private bool _busy;
    private string? _addIdempotencyKey;
    private string? _addIdempotencyFingerprint;
    private string? _redeemIdempotencyKey;
    private string? _redeemIdempotencyFingerprint;

    public LoyaltyPage()
    {
        InitializeComponent();
        _offlinePolicy = new ClientOfflinePolicy(_cache);
        _loyalty = new MotherLoyaltyClient(_cache, _offlinePolicy);

        Loyalty.ShowDiagnostics = true;
        Loyalty.SetCustomer(null);
        Loyalty.SearchRequested += async (_, _) => await OnSearchAsync();
        Loyalty.TestConnectionRequested += async (_, _) => await OnTestConnectionAsync();
        Loyalty.NewCustomerRequested += (_, _) => Loyalty.ShowNewCustomer(true, Loyalty.PhoneSearchText);
        Loyalty.CreateCustomerRequested += async (_, request) => await OnCreateCustomerAsync(request);
        Loyalty.AddPointsRequested += async (_, _) => await OnAdjustPointsAsync(addPoints: true);
        Loyalty.RedeemPointsRequested += async (_, _) => await OnAdjustPointsAsync(addPoints: false);
        Loyalty.ViewHistoryRequested += async (_, _) => await OnViewHistoryAsync();
        Loyalty.SendStatementRequested += async (_, _) =>
            await DisplayAlert("Loyalty", "Loyalty statements are sent from Mother POS / OrderWeb cloud.", "OK");

        TopBar.MenuClicked += async (_, _) => await OpenSidebarAsync();
        TopBar.LogoutClicked += async (_, _) => await Navigation.PopToRootAsync(false);
        Sidebar.MenuItemSelected += async (_, menu) => await NavigateFromSidebarAsync(menu);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RefreshAccessFromMotherAsync();
        if (!HasLoyaltyAccess())
        {
            await DisplayAlert(
                "Loyalty",
                "This Client terminal is not allowed to use Loyalty. On Mother: Terminal Health → Access → Loyalty ON → Save, then try again (or Update All).",
                "OK");
            await Navigation.PopAsync(false);
        }
    }

    private static bool HasLoyaltyAccess() =>
        ClientHostAccess.Features.Contains(PosFeatureKeys.CustomerPoints) ||
        ClientHostAccess.CanOpenMenu("Loyalty Points") ||
        ClientHostAccess.CanOpenMenu("Loyalty");

    private async Task RefreshAccessFromMotherAsync()
    {
        var accessClient = new MotherAccessClient(_cache);
        await accessClient.RefreshAccessAsync();
    }

    private async Task OnSearchAsync()
    {
        var lookup = Loyalty.PhoneSearchText;
        if (string.IsNullOrWhiteSpace(lookup))
        {
            await DisplayAlert("Loyalty", "Enter a phone number or loyalty card number.", "OK");
            return;
        }

        if (_busy)
        {
            return;
        }

        if (!await EnsureReadyAsync())
        {
            return;
        }

        _busy = true;
        Loyalty.IsSearchEnabled = false;
        try
        {
            var result = await _loyalty.SearchAsync(lookup);
            if (result.Success && result.Customer != null)
            {
                ClientLoyaltyDiagnostics.Record("search", true, result.Message ?? "Customer found.");
                ApplyCustomer(result.Customer, result.History, lookup);
                return;
            }

            Loyalty.SetCustomer(null);
            _currentCustomer = null;
            _currentLookup = null;

            var queued = IsQueued(result.ErrorCode);
            ClientLoyaltyDiagnostics.Record("search", false, result.Error ?? result.Message, result.ErrorCode, queued);

            if (string.Equals(result.ErrorCode, LoyaltyErrorCodes.OfflineMother, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(result.ErrorCode, LoyaltyErrorCodes.CloudDown, StringComparison.OrdinalIgnoreCase) ||
                queued)
            {
                await DisplayAlert("Loyalty", FormatError(result), "OK");
                return;
            }

            if (string.Equals(result.ErrorCode, LoyaltyErrorCodes.CustomerNotFound, StringComparison.OrdinalIgnoreCase) ||
                !result.CustomerExists)
            {
                var create = await DisplayAlert(
                    "Customer Not Found",
                    $"{FormatError(result)}\n\nLookup: {lookup}\n\nCreate a new loyalty customer?",
                    "Create",
                    "Cancel");
                if (create)
                {
                    Loyalty.ShowNewCustomer(true, lookup);
                }

                return;
            }

            await DisplayAlert("Loyalty", FormatError(result), "OK");
        }
        finally
        {
            Loyalty.IsSearchEnabled = true;
            _busy = false;
        }
    }

    private async Task OnTestConnectionAsync()
    {
        if (_busy)
        {
            return;
        }

        if (!await EnsureReadyAsync())
        {
            return;
        }

        _busy = true;
        try
        {
            var lookup = Loyalty.PhoneSearchText;
            var result = await _loyalty.TestConnectionAsync(string.IsNullOrWhiteSpace(lookup) ? null : lookup);
            ClientLoyaltyDiagnostics.Record(
                "test",
                result.Success,
                result.Message ?? result.Error,
                result.ErrorCode,
                IsQueued(result.ErrorCode));
            await DisplayAlert(
                result.Success ? "Loyalty test" : "Loyalty test failed",
                result.Success
                    ? (result.Message ?? "Loyalty connection OK.")
                    : FormatError(result.Error, result.Message, result.ErrorCode),
                "OK");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task OnCreateCustomerAsync(LoyaltyNewCustomerRequest request)
    {
        if (!HasLoyaltyAccess())
        {
            await DisplayAlert("Loyalty", "This Client terminal is not allowed to create loyalty customers.", "OK");
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Name))
        {
            await DisplayAlert("Loyalty", "Phone number and customer name are required.", "OK");
            return;
        }

        if (_busy)
        {
            return;
        }

        if (!await EnsureReadyAsync())
        {
            return;
        }

        _busy = true;
        try
        {
            Loyalty.ShowNewCustomer(false);
            var result = await _loyalty.CreateCustomerAsync(request.Phone, request.Name, request.Email);
            if (!result.Success || result.Customer == null)
            {
                ClientLoyaltyDiagnostics.Record("create", false, result.Error ?? result.Message, result.ErrorCode, IsQueued(result.ErrorCode));
                await DisplayAlert("Loyalty", FormatError(result), "OK");
                return;
            }

            ClientLoyaltyDiagnostics.Record("create", true, result.Message ?? "Customer created.");
            Loyalty.PhoneSearchText = request.Phone;
            ApplyCustomer(result.Customer, result.History, request.Phone);
            var card = string.IsNullOrWhiteSpace(result.Customer.LoyaltyCardNumber)
                ? "assigned by cloud"
                : result.Customer.LoyaltyCardNumber;
            await DisplayAlert(
                "Customer created",
                $"Name: {result.Customer.Name}\nPhone: {result.Customer.DisplayPhone ?? result.Customer.Phone}\nCard: {card}\nPoints: {result.Customer.PointsBalance:N0}",
                "OK");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task OnAdjustPointsAsync(bool addPoints)
    {
        if (_busy)
        {
            return;
        }

        if (_currentCustomer == null || string.IsNullOrWhiteSpace(_currentLookup))
        {
            await DisplayAlert("Loyalty", "Search and select a customer first.", "OK");
            return;
        }

        if (!int.TryParse(Loyalty.PointsText, out var points) || points <= 0)
        {
            await DisplayAlert("Loyalty", "Enter a valid positive points amount.", "OK");
            return;
        }

        var balance = _currentCustomer.PointsBalance;
        if (!addPoints && points > balance)
        {
            await DisplayAlert("Loyalty", $"Insufficient points. Customer has {balance:N0} points.", "OK");
            return;
        }

        if (!await EnsureReadyAsync())
        {
            return;
        }

        var notes = Loyalty.NotesText;
        var reason = string.IsNullOrWhiteSpace(notes)
            ? addPoints
                ? $"Client POS add — {points} points"
                : $"Client POS redeem — £{points / 100m:F2}"
            : notes.Trim();

        var customerName = string.IsNullOrWhiteSpace(_currentCustomer.Name)
            ? (_currentCustomer.DisplayPhone ?? _currentCustomer.Phone ?? _currentLookup)
            : _currentCustomer.Name;
        var newBalance = addPoints ? balance + points : balance - points;
        var confirm = await DisplayAlert(
            addPoints ? "Confirm add points" : "Confirm redeem points",
            $"{(addPoints ? "Add" : "Redeem")} {points:N0} points for {customerName}?\n\n" +
            $"Current: {balance:N0}\nAfter: {newBalance:N0}\n\nPoints change only when Mother confirms with OrderWeb cloud.",
            addPoints ? "Add" : "Redeem",
            "Cancel");
        if (!confirm)
        {
            return;
        }

        if (_busy)
        {
            return;
        }

        _busy = true;
        Loyalty.SetPointsBusy(true, addPoints);
        try
        {
            var operation = addPoints ? "add" : "redeem";
            var fingerprint = $"{operation}|{_currentLookup}|{points}|{reason}";
            string idempotencyKey;
            if (addPoints)
            {
                idempotencyKey = GetStickyKey(
                    ref _addIdempotencyKey,
                    ref _addIdempotencyFingerprint,
                    fingerprint,
                    $"client-loyalty-add:{_currentLookup}:{points}:{Guid.NewGuid():N}");
            }
            else
            {
                idempotencyKey = GetStickyKey(
                    ref _redeemIdempotencyKey,
                    ref _redeemIdempotencyFingerprint,
                    fingerprint,
                    $"client-loyalty-redeem:{_currentLookup}:{points}:{Guid.NewGuid():N}");
            }

            var result = addPoints
                ? await _loyalty.AddPointsAsync(_currentLookup, points, reason, idempotencyKey)
                : await _loyalty.RedeemPointsAsync(_currentLookup, points, reason, idempotencyKey);

            if (!result.Success)
            {
                var queued = IsQueued(result.ErrorCode);
                ClientLoyaltyDiagnostics.Record(operation, false, result.Error ?? result.Message, result.ErrorCode, queued);
                await DisplayAlert("Loyalty", FormatError(result), "OK");
                return;
            }

            ClientLoyaltyDiagnostics.Record(operation, true, result.Message ?? $"Points {operation} OK.");
            if (result.Customer != null)
            {
                ApplyCustomer(result.Customer, result.History, _currentLookup);
            }
            else if (result.PointsBalance.HasValue && _currentCustomer != null)
            {
                ApplyCustomer(
                    _currentCustomer with { PointsBalance = result.PointsBalance.Value },
                    result.History,
                    _currentLookup);
            }

            Loyalty.ClearAdjustmentFields();
            if (addPoints)
            {
                ClearStickyKey(ref _addIdempotencyKey, ref _addIdempotencyFingerprint);
            }
            else
            {
                ClearStickyKey(ref _redeemIdempotencyKey, ref _redeemIdempotencyFingerprint);
            }

            var shownBalance = result.PointsBalance ?? result.Customer?.PointsBalance ?? newBalance;
            await DisplayAlert(
                addPoints ? "Points added" : "Points redeemed",
                result.Message ?? $"Cloud balance is now {shownBalance:N0} points.",
                "OK");
        }
        finally
        {
            Loyalty.SetPointsBusy(false);
            _busy = false;
        }
    }

    private async Task OnViewHistoryAsync()
    {
        if (_busy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_currentLookup))
        {
            await DisplayAlert("Loyalty", "Search and select a customer first.", "OK");
            return;
        }

        if (!await EnsureReadyAsync())
        {
            return;
        }

        _busy = true;
        Loyalty.ShowHistory(true);
        try
        {
            var result = await _loyalty.HistoryAsync(_currentLookup);
            if (!result.Success)
            {
                ClientLoyaltyDiagnostics.Record("history", false, result.Error ?? result.Message, result.ErrorCode, IsQueued(result.ErrorCode));
                Loyalty.SetHistory([]);
                await DisplayAlert("Loyalty", FormatError(result), "OK");
                return;
            }

            ClientLoyaltyDiagnostics.Record("history", true, result.Message ?? "History loaded.");
            if (result.Customer != null)
            {
                ApplyCustomer(result.Customer, result.History, _currentLookup);
            }
            else
            {
                Loyalty.SetHistory(MapHistory(result.History));
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task<bool> EnsureReadyAsync()
    {
        var access = await new MotherAccessClient(_cache).RefreshAccessAsync();
        if (!HasLoyaltyAccess())
        {
            var message = access.Success
                ? "This Client terminal is not allowed to use Loyalty. On Mother: Terminal Health → Access → Loyalty ON → Save, then try again."
                : $"Could not refresh Client access from Mother. {access.Message}";
            await DisplayAlert("Loyalty", message, "OK");
            return false;
        }

        var online = await _offlinePolicy.IsMotherOnlineAsync();
        var decision = _offlinePolicy.Evaluate(ClientOperation.Loyalty, online);
        if (!decision.Allowed)
        {
            ClientLoyaltyDiagnostics.Record("gate", false, decision.Message, LoyaltyErrorCodes.OfflineMother);
            await DisplayAlert("Loyalty", decision.Message, "OK");
            return false;
        }

        return true;
    }

    private void ApplyCustomer(
        ClientLoyaltyCustomerDto customer,
        IReadOnlyList<ClientLoyaltyHistoryItemDto>? history,
        string lookup)
    {
        _currentCustomer = customer;
        _currentLookup = string.IsNullOrWhiteSpace(customer.Phone) ? lookup : customer.Phone;

        var phone = string.IsNullOrWhiteSpace(customer.DisplayPhone)
            ? (customer.Phone ?? lookup)
            : customer.DisplayPhone;
        if (!string.IsNullOrWhiteSpace(customer.LoyaltyCardNumber))
        {
            phone = $"{phone} · Card {customer.LoyaltyCardNumber}";
        }

        Loyalty.SetCustomer(new LoyaltyCustomerPresentation(
            string.IsNullOrWhiteSpace(customer.Name) ? "No name saved" : customer.Name.Trim(),
            string.IsNullOrWhiteSpace(phone) ? "Phone not saved" : phone.Trim(),
            string.IsNullOrWhiteSpace(customer.Email) ? "Email not saved" : customer.Email.Trim(),
            customer.PointsBalance,
            "Balance from OrderWeb cloud via Mother"));

        if (history != null)
        {
            Loyalty.SetHistory(MapHistory(history));
        }
    }

    private static IReadOnlyList<LoyaltyHistoryPresentation> MapHistory(
        IReadOnlyList<ClientLoyaltyHistoryItemDto>? history)
    {
        if (history is null || history.Count == 0)
        {
            return [];
        }

        return history.Select(tx =>
        {
            var description = string.IsNullOrWhiteSpace(tx.Description)
                ? (string.IsNullOrWhiteSpace(tx.TransactionType) ? "Points update" : tx.TransactionType!)
                : tx.Description!;
            var pointsDisplay = tx.PointsChange > 0 ? $"+{tx.PointsChange} pts" : $"{tx.PointsChange} pts";
            return new LoyaltyHistoryPresentation(
                description,
                tx.CreatedAt == default ? "—" : tx.CreatedAt.ToString("MMM dd, yyyy HH:mm"),
                pointsDisplay,
                tx.PointsChange >= 0);
        }).ToList();
    }

    private static bool IsQueued(string? errorCode) =>
        string.Equals(errorCode, LoyaltyErrorCodes.Queued, StringComparison.OrdinalIgnoreCase);

    private static string FormatError(ClientLoyaltyLookupResponseDto result) =>
        FormatError(result.Error, result.Message, result.ErrorCode);

    private static string FormatError(string? error, string? message, string? errorCode)
    {
        var text = !string.IsNullOrWhiteSpace(error) ? error! :
            !string.IsNullOrWhiteSpace(message) ? message! :
            "Loyalty request failed.";

        if (string.Equals(errorCode, LoyaltyErrorCodes.OfflineMother, StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(text)
                ? "Loyalty operations require Mother POS. No points were changed."
                : text;
        }

        if (string.Equals(errorCode, LoyaltyErrorCodes.Queued, StringComparison.OrdinalIgnoreCase))
        {
            return text.Contains("queued", StringComparison.OrdinalIgnoreCase)
                ? text
                : $"{text}\n\nMother queued a cloud retry. Do not submit again — retry keeps the same idempotency key.";
        }

        if (string.Equals(errorCode, LoyaltyErrorCodes.CloudDown, StringComparison.OrdinalIgnoreCase))
        {
            return text.Contains("queued", StringComparison.OrdinalIgnoreCase)
                ? text
                : $"{text}\n\nOrderWeb cloud is unavailable through Mother. No points were changed.";
        }

        if (string.Equals(errorCode, LoyaltyErrorCodes.AccessDenied, StringComparison.OrdinalIgnoreCase))
        {
            return $"{text}\n\nAsk Mother to grant Loyalty access for this terminal.";
        }

        return string.IsNullOrWhiteSpace(errorCode) ? text : $"{text} ({errorCode})";
    }

    private static string GetStickyKey(
        ref string? key,
        ref string? fingerprint,
        string currentFingerprint,
        string factory)
    {
        if (!string.Equals(fingerprint, currentFingerprint, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(key))
        {
            fingerprint = currentFingerprint;
            key = factory;
        }

        return key;
    }

    private static void ClearStickyKey(ref string? key, ref string? fingerprint)
    {
        key = null;
        fingerprint = null;
    }

    private async void OnBackdropTapped(object sender, TappedEventArgs e) => await CloseSidebarAsync();

    private async Task OpenSidebarAsync()
    {
        SidebarLayer.IsVisible = true;
        Sidebar.TranslationX = -280;
        await Sidebar.TranslateTo(0, 0, 180, Easing.CubicOut);
    }

    private async Task CloseSidebarAsync()
    {
        await Sidebar.TranslateTo(-280, 0, 160, Easing.CubicIn);
        SidebarLayer.IsVisible = false;
    }

    private async Task NavigateFromSidebarAsync(string menu)
    {
        await CloseSidebarAsync();
        if (ClientSidebarNavigation.IsDashboard(menu))
        {
            await Navigation.PopToRootAsync(false);
            return;
        }

        if (await ClientSidebarNavigation.TryHandleMotherOnlyAsync(this, menu))
        {
            return;
        }

        if (ClientHostAccess.IsMenuRoute(menu, "loyalty") ||
            !ClientHostAccess.CanOpenMenu(menu))
        {
            return;
        }

        if (ClientSidebarNavigation.IsCustomerSurface(menu))
        {
            await Navigation.PopToRootAsync(false);
            return;
        }

        var page = ClientSidebarNavigation.CreatePage(menu);
        if (page is not null)
        {
            await Navigation.PushAsync(page, false);
        }
    }
}

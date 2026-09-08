using OrderWeb.Contracts.Capabilities;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;
using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>Adapts Mother local auth to the shared authentication contract.</summary>
public sealed class MotherAuthenticationService : IAuthenticationService
{
    private readonly AuthenticationService _inner;

    public MotherAuthenticationService(AuthenticationService inner)
    {
        _inner = inner;
    }

    public async Task<OperationResult<UserSession>> LoginAsync(AuthenticationRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Method, "PIN", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(request.Pin))
        {
            return OperationResult<UserSession>.Failure(new OperationError(OperationErrorCode.Validation, "Enter a 4 digit PIN."));
        }

        var result = await _inner.LoginAsync(request.Pin, request.Pin);
        if (!result.Success || result.User is null)
        {
            return OperationResult<UserSession>.Failure(new OperationError(OperationErrorCode.Unauthorized, result.Message ?? "Wrong PIN. Try again."));
        }

        if (result.User.Role == UserRole.Staff)
        {
            await _inner.LogoutAsync();
            return OperationResult<UserSession>.Failure(new OperationError(OperationErrorCode.Forbidden, "Staff PIN is for Clock In/Out only."));
        }

        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var user = new AuthenticatedUser(
            result.User.Id.ToString(),
            result.User.Name,
            result.User.Role.ToString(),
            permissions);

        var session = new UserSession(
            Guid.NewGuid().ToString("N"),
            user,
            DateTimeOffset.UtcNow.AddHours(12),
            IsAuthoritative: true);

        return OperationResult<UserSession>.Success(session);
    }

    public async Task<OperationResult> LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _inner.LogoutAsync();
        return OperationResult.Success();
    }
}

public static class MotherCapabilityResolver
{
    public static IReadOnlySet<string> ForRole(UserRole? role)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (role is null) return set;

        set.Add(PosCapabilityKeys.ViewDashboard);
        set.Add(PosCapabilityKeys.ViewCustomers);
        set.Add(PosCapabilityKeys.CreateOrders);
        set.Add(PosCapabilityKeys.SubmitOrders);
        set.Add(PosCapabilityKeys.TakeOrders);
        set.Add(PosCapabilityKeys.OpenTables);
        set.Add(PosCapabilityKeys.ManageCustomers);
        // Waiters on Client POS must be able to fire kitchen/bar tickets,
        // customer receipts, and cash Collection payments through Mother.
        // Printer setup and admin stay Admin-only.
        set.Add(PosCapabilityKeys.PrintReceipts);
        set.Add(PosCapabilityKeys.TakePayments);

        // Cashiers have a reports-and-till-only surface. Deliberately do not
        // grant any order, customer, payment, refund, discount, or approval
        // capability to this role.
        if (role is UserRole.Cashier)
        {
            set.Clear();
            set.Add(PosCapabilityKeys.ViewDashboard);
            set.Add(PosCapabilityKeys.ViewReports);
            set.Add(PosCapabilityKeys.ViewDailyReports);
            set.Add(PosCapabilityKeys.PreviewZReports);
            set.Add(PosCapabilityKeys.PrintZReports);
            set.Add(PosCapabilityKeys.ReprintZReports);
            set.Add(PosCapabilityKeys.ExportReports);
            set.Add(PosCapabilityKeys.ViewReportPrintHistory);
            set.Add(PosCapabilityKeys.OpenCashDrawer);
            set.Add(PosCapabilityKeys.ReconcileCashDrawer);
            set.Add(PosCapabilityKeys.AddReconciliationNotes);
            return set;
        }

        if (role is UserRole.Manager or UserRole.Admin)
        {
            set.Add(PosCapabilityKeys.TakePayments);
            set.Add(PosCapabilityKeys.ApplyDiscount);
            set.Add(PosCapabilityKeys.VoidItems);
            set.Add(PosCapabilityKeys.VoidOrders);
            set.Add(PosCapabilityKeys.Refund);
            set.Add(PosCapabilityKeys.TransferTables);
            set.Add(PosCapabilityKeys.ViewReports);
            set.Add(PosCapabilityKeys.PrintReceipts);
            set.Add(PosCapabilityKeys.ReprintReceipts);
            set.Add(PosCapabilityKeys.OpenCashDrawer);
            set.Add(PosCapabilityKeys.ApproveManagerAction);
        }

        if (role is UserRole.Admin)
        {
            set.Add(PosCapabilityKeys.ManageUsers);
            set.Add(PosCapabilityKeys.EditMenu);
            set.Add(PosCapabilityKeys.ConfigurePrinters);
            set.Add(PosCapabilityKeys.AccessAdmin);
            set.Add(PosCapabilityKeys.EditTables);
            set.Add(PosCapabilityKeys.AccessSettings);
        }

        return set;
    }
}

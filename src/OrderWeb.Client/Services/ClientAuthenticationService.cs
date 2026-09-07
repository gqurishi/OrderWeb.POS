using OrderWeb.Client.Models;
using OrderWeb.Contracts.Capabilities;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;

namespace OrderWeb.Client.Services;

public sealed class ClientAuthenticationService : IAuthenticationService
{
    private readonly MotherAuthClient _authClient;

    public ClientAuthenticationService(MotherAuthClient authClient)
    {
        _authClient = authClient;
    }

    public async Task<OperationResult<UserSession>> LoginAsync(AuthenticationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!string.Equals(request.Method, "PIN", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(request.Pin))
            {
                return OperationResult<UserSession>.Failure(new OperationError(OperationErrorCode.Validation, "Enter a 4 digit PIN."));
            }

            var session = await _authClient.LoginAsync(new LoginRequest("PIN", request.Pin));
            if (IsMotherOnlyRole(session.Role))
            {
                return OperationResult<UserSession>.Failure(new OperationError(
                    OperationErrorCode.Forbidden,
                    "Administrator access is available on the Mother POS only. Please use the Mother POS terminal."));
            }
            if (string.Equals(session.Role, "Staff", StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<UserSession>.Failure(new OperationError(OperationErrorCode.Forbidden, "Staff PIN is for Clock In/Out only."));
            }

            var permissions = session.Permissions?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                              ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var user = new AuthenticatedUser(
                session.UserId,
                session.UserName,
                session.Role,
                permissions);

            return OperationResult<UserSession>.Success(new UserSession(
                session.SessionToken,
                user,
                session.ExpiresAtUtc,
                IsAuthoritative: true));
        }
        catch (LoginException ex)
        {
            var code = ex.Message.Contains("not paired", StringComparison.OrdinalIgnoreCase)
                ? OperationErrorCode.Offline
                : OperationErrorCode.Unauthorized;
            return OperationResult<UserSession>.Failure(new OperationError(code, ex.Message));
        }
        catch (Exception ex)
        {
            return OperationResult<UserSession>.Failure(new OperationError(OperationErrorCode.ServerError, ex.Message));
        }
    }

    public Task<OperationResult> LogoutAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(OperationResult.Success());

    private static bool IsMotherOnlyRole(string? role) =>
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase);
}

public static class ClientCapabilityResolver
{
    public static IReadOnlySet<string> ForRole(string? role, IEnumerable<string>? permissions = null)
    {
        // Administrators never have a Client POS surface. Keep this guard even
        // when talking to an older Mother version that might send capabilities.
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(role, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        // Mother sends explicit pos.* capabilities after Client authentication.
        // When they are present, they are authoritative for Client presentation;
        // the role mapping below is only a compatibility fallback for old sessions.
        var explicitCapabilities = permissions?
            .Where(permission => permission.StartsWith("pos.", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (explicitCapabilities is { Count: > 0 })
        {
            return explicitCapabilities;
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PosCapabilityKeys.ViewDashboard,
            PosCapabilityKeys.ViewCustomers,
            PosCapabilityKeys.CreateOrders,
            PosCapabilityKeys.SubmitOrders,
            PosCapabilityKeys.TakeOrders,
            PosCapabilityKeys.OpenTables,
            PosCapabilityKeys.ManageCustomers
        };

        if (string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
        {
            set.Add(PosCapabilityKeys.TakePayments);
            set.Add(PosCapabilityKeys.ApplyDiscount);
            set.Add(PosCapabilityKeys.VoidItems);
            set.Add(PosCapabilityKeys.VoidOrders);
            set.Add(PosCapabilityKeys.Refund);
            set.Add(PosCapabilityKeys.TransferTables);
            set.Add(PosCapabilityKeys.PrintReceipts);
            set.Add(PosCapabilityKeys.ReprintReceipts);
            set.Add(PosCapabilityKeys.OpenCashDrawer);
            set.Add(PosCapabilityKeys.ApproveManagerAction);
        }

        return set;
    }
}

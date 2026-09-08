using OrderWeb.Client.Models;
using OrderWeb.Contracts.Access;
using OrderWeb.Contracts.Capabilities;
using OrderWeb.Contracts.Dtos;
using OrderWeb.Contracts.Results;
using OrderWeb.Contracts.Services;

namespace OrderWeb.Client.Services;

public sealed class ClientAuthenticationService : IAuthenticationService
{
    private readonly MotherAuthClient _authClient;
    private readonly Func<bool>? _isMotherOnline;

    public ClientAuthenticationService(MotherAuthClient authClient, Func<bool>? isMotherOnline = null)
    {
        _authClient = authClient;
        _isMotherOnline = isMotherOnline;
    }

    /// <summary>Last Mother login payload (features/routes) after a successful PIN auth.</summary>
    public LoginSession? LastSuccessfulLogin { get; private set; }

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
                    "Admin can't sign in on Client POS. Use Mother POS. This terminal is for User, Manager, and Cashier.",
                    Detail: "admin_mother_only"));
            }
            if (string.Equals(session.Role, "Staff", StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<UserSession>.Failure(new OperationError(OperationErrorCode.Forbidden, "Staff PIN is for Clock In/Out only."));
            }

            if (string.Equals(session.Role, "Cashier", StringComparison.OrdinalIgnoreCase) &&
                _isMotherOnline is not null &&
                !_isMotherOnline())
            {
                return OperationResult<UserSession>.Failure(new OperationError(
                    OperationErrorCode.Offline,
                    "Cashier access requires a live connection to Mother POS."));
            }

            var permissions = session.Permissions?.ToHashSet(StringComparer.OrdinalIgnoreCase)
                              ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var user = new AuthenticatedUser(
                session.UserId,
                session.UserName,
                session.Role,
                permissions);

            LastSuccessfulLogin = session;
            return OperationResult<UserSession>.Success(new UserSession(
                session.SessionToken,
                user,
                session.ExpiresAtUtc,
                IsAuthoritative: true));
        }
        catch (LoginException ex)
        {
            LastSuccessfulLogin = null;
            if (string.Equals(ex.ErrorCode, "admin_mother_only", StringComparison.OrdinalIgnoreCase))
            {
                return OperationResult<UserSession>.Failure(new OperationError(
                    OperationErrorCode.Forbidden,
                    string.IsNullOrWhiteSpace(ex.Message)
                        ? "Admin can't sign in on Client POS. Use Mother POS."
                        : ex.Message,
                    Detail: "admin_mother_only"));
            }

            var code = ex.IsPairingInvalid || MotherAuthClient.ContainsPairingFailure(ex.Message)
                ? OperationErrorCode.Forbidden
                : ex.Message.Contains("not paired", StringComparison.OrdinalIgnoreCase)
                    ? OperationErrorCode.Offline
                    : OperationErrorCode.Unauthorized;
            return OperationResult<UserSession>.Failure(new OperationError(code, ex.Message, Detail: ex.ErrorCode));
        }
        catch (Exception ex)
        {
            LastSuccessfulLogin = null;
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
            return ClientAccessPolicy.FilterCapabilities(explicitCapabilities);
        }

        // Never fall through to the order-taking defaults for Cashier. A Mother
        // upgrade that omits capabilities must fail closed on Client POS.
        if (string.Equals(role, "Cashier", StringComparison.OrdinalIgnoreCase))
        {
            return ClientAccessPolicy.FilterCapabilities(
            [
                PosCapabilityKeys.ViewDashboard
            ]);
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            PosCapabilityKeys.ViewDashboard,
            PosCapabilityKeys.ViewCustomers,
            PosCapabilityKeys.CreateOrders,
            PosCapabilityKeys.SubmitOrders,
            PosCapabilityKeys.TakeOrders,
            PosCapabilityKeys.OpenTables,
            PosCapabilityKeys.ManageCustomers,
            PosCapabilityKeys.PrintReceipts,
            PosCapabilityKeys.TakePayments
        };

        if (string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
        {
            set.Add(PosCapabilityKeys.TakePayments);
            set.Add(PosCapabilityKeys.ApplyDiscount);
            set.Add(PosCapabilityKeys.VoidItems);
            set.Add(PosCapabilityKeys.VoidOrders);
            set.Add(PosCapabilityKeys.Refund);
            set.Add(PosCapabilityKeys.TransferTables);
            set.Add(PosCapabilityKeys.ReprintReceipts);
            set.Add(PosCapabilityKeys.OpenCashDrawer);
            set.Add(PosCapabilityKeys.ApproveManagerAction);
        }

        return ClientAccessPolicy.FilterCapabilities(set);
    }
}

using POS_in_NET.Models;

namespace POS_in_NET.Services;

/// <summary>
/// Mother host authentication facade used by the shared login presentation.
/// Keeps database-backed validation inside Mother while SharedUI stays presentation-only.
/// </summary>
public sealed class MotherAuthenticationService
{
    private static readonly Lazy<MotherAuthenticationService> LazyInstance =
        new(() => new MotherAuthenticationService());

    private readonly AuthenticationService _inner;

    public static MotherAuthenticationService Instance => LazyInstance.Value;

    public MotherAuthenticationService()
        : this(AuthenticationService.Instance)
    {
    }

    public MotherAuthenticationService(AuthenticationService authenticationService)
    {
        _inner = authenticationService ?? throw new ArgumentNullException(nameof(authenticationService));
    }

    public User? CurrentUser => _inner.CurrentUser;
    public bool IsAuthenticated => _inner.IsAuthenticated;

    public Task WarmCacheAsync() => _inner.WarmAuthenticationCacheAsync();

    public Task<bool> HasAnyUserAsync() => _inner.HasAnyUserAsync();

    public Task<(bool Success, string Message, User? User)> LoginWithPinAsync(string pin)
    {
        pin = pin?.Trim() ?? string.Empty;
        return _inner.LoginAsync(pin, pin);
    }

    public Task<(bool Success, string Message, User? User)> ValidatePinAsync(string pin)
        => _inner.ValidatePinAsync(pin);

    public Task LogoutAsync() => _inner.LogoutAsync();
}

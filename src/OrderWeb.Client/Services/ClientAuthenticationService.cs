using OrderWeb.Client.Models;

namespace OrderWeb.Client.Services;

/// <summary>
/// Client host authentication facade used by the shared login presentation.
/// Validates PIN through Mother API; pairing remains a separate Client workflow.
/// </summary>
public sealed class ClientAuthenticationService
{
    private readonly MotherAuthClient _motherAuth;

    public ClientAuthenticationService()
        : this(new MotherAuthClient())
    {
    }

    public ClientAuthenticationService(MotherAuthClient motherAuth)
    {
        _motherAuth = motherAuth ?? throw new ArgumentNullException(nameof(motherAuth));
    }

    public Task<(bool Success, string Message, LoginSession? Session)> LoginWithPinAsync(string pin)
        => _motherAuth.ValidatePinAsync(pin);

    public Task<LoginSession> LoginAsync(LoginRequest request)
        => _motherAuth.LoginAsync(request);
}

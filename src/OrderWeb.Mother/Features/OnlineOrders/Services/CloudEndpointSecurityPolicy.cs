namespace POS_in_NET.Services;

public static class CloudEndpointSecurityPolicy
{
    public static (bool IsValid, string Message) Validate(string? restApiUrl, string? webSocketUrl)
    {
        if (!TryValidate(restApiUrl, "https", "REST API", out var restMessage))
        {
            return (false, restMessage);
        }

        if (!TryValidate(webSocketUrl, "wss", "WebSocket", out var socketMessage))
        {
            return (false, socketMessage);
        }

        return (true, "Cloud endpoints use encrypted transport.");
    }

    public static bool IsSecureRestApiUrl(string? value) =>
        TryValidate(value, "https", "REST API", out _);

    private static bool TryValidate(string? value, string requiredScheme, string label, out string message)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, requiredScheme, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            message = $"{label} URL must be an absolute {requiredScheme}:// address.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            message = $"{label} URL must not contain embedded credentials.";
            return false;
        }

        var query = uri.Query;
        if (query.Contains("apikey=", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("api_key=", StringComparison.OrdinalIgnoreCase) ||
            query.Contains("token=", StringComparison.OrdinalIgnoreCase))
        {
            message = $"{label} URL must not store API keys or tokens in its query string.";
            return false;
        }

        message = string.Empty;
        return true;
    }
}

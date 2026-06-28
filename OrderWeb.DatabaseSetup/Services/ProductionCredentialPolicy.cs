using OrderWeb.DatabaseSetup.Models;

namespace OrderWeb.DatabaseSetup.Services;

public static class ProductionCredentialPolicy
{
    public static (bool IsValid, string Message) ValidateAppCredentials(string databaseUser, string databasePassword)
    {
        if (string.IsNullOrWhiteSpace(databaseUser))
        {
            return (false, "Database user is required.");
        }

        if (string.IsNullOrWhiteSpace(databasePassword))
        {
            return (false, "Database password is required.");
        }

        return (true, "OK");
    }

    public static bool IsForbiddenRootPasswordForApp(string? password) =>
        string.Equals(password?.Trim(), "root", StringComparison.OrdinalIgnoreCase);
}

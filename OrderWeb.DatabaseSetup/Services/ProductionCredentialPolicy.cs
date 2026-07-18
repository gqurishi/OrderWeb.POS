using OrderWeb.DatabaseSetup.Models;

namespace OrderWeb.DatabaseSetup.Services;

public static class ProductionCredentialPolicy
{
    public const int MinimumPasswordLength = 24;

    public static (bool IsValid, string Message) ValidateAppCredentials(string databaseUser, string databasePassword)
    {
        if (!string.Equals(databaseUser?.Trim(), PosDefaults.ProductionDatabaseUser, StringComparison.Ordinal))
        {
            return (false, $"The application database user must be '{PosDefaults.ProductionDatabaseUser}'; root/admin accounts are not allowed.");
        }

        if (string.IsNullOrWhiteSpace(databasePassword) || databasePassword.Length < MinimumPasswordLength)
        {
            return (false, $"The application database password must be at least {MinimumPasswordLength} characters.");
        }

        if (!databasePassword.Any(char.IsUpper)
            || !databasePassword.Any(char.IsLower)
            || !databasePassword.Any(char.IsDigit)
            || !databasePassword.Any(character => !char.IsLetterOrDigit(character)))
        {
            return (false, "The application database password must contain upper-case, lower-case, numeric, and special characters.");
        }

        return (true, "OK");
    }
}

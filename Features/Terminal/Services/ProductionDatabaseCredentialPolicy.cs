using POS_in_NET.Models;

namespace POS_in_NET.Services;

public static class ProductionDatabaseCredentialPolicy
{
    public static (bool IsValid, string Message) Validate(string databaseUser, string databasePassword)
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
}

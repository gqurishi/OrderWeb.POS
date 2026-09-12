namespace OrderWeb.Contracts.Access;

/// <summary>
/// Idle logout for User, Manager, and Cashier. Mother stores it. Client reads it.
/// Empty or missing means 3 minutes. Allowed range is 3 to 60.
/// </summary>
public static class TillLogoutMinutes
{
    public const int DefaultMinutes = 3;
    public const int MinimumMinutes = 3;
    public const int MaximumMinutes = 60;

    public static int Normalize(int? minutes)
    {
        if (minutes is null or < MinimumMinutes)
        {
            return DefaultMinutes;
        }

        return minutes > MaximumMinutes ? MaximumMinutes : minutes.Value;
    }

    public static bool TryParse(string? text, out int minutes, out string? error)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            minutes = DefaultMinutes;
            error = null;
            return true;
        }

        if (!int.TryParse(text.Trim(), out var parsed))
        {
            minutes = DefaultMinutes;
            error = "Enter a whole number of minutes.";
            return false;
        }

        if (parsed < MinimumMinutes || parsed > MaximumMinutes)
        {
            minutes = DefaultMinutes;
            error = "Logout time must be from 3 to 60 minutes.";
            return false;
        }

        minutes = parsed;
        error = null;
        return true;
    }
}

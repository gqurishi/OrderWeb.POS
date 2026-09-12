using OrderWeb.Contracts.Access;

namespace OrderWeb.Client.Services;

/// <summary>Last logout minutes received from Mother. Missing value stays at 3.</summary>
public static class ClientTillLogoutStore
{
    private const string Key = "orderweb.client.till-logout-minutes";

    public static int GetMinutes()
    {
        var saved = Preferences.Get(Key, TillLogoutMinutes.DefaultMinutes);
        return TillLogoutMinutes.Normalize(saved);
    }

    public static void Save(int minutes)
    {
        Preferences.Set(Key, TillLogoutMinutes.Normalize(minutes));
    }
}

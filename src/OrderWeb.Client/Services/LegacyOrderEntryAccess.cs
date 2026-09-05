namespace OrderWeb.Client.Services;

/// <summary>
/// Temporary rollback switch for Step 7. Normal navigation uses SharedUI
/// <c>OrderEntryView</c>. Enable this preference only while validating the cutover.
/// </summary>
public static class LegacyOrderEntryAccess
{
    public const string PreferenceKey = "client.order_entry.use_legacy_rollback";

    public static bool PreferLegacyRollback =>
        Preferences.Default.Get(PreferenceKey, false);

    public static void SetLegacyRollback(bool enabled) =>
        Preferences.Default.Set(PreferenceKey, enabled);
}

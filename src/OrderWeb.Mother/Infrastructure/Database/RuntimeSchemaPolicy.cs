namespace POS_in_NET.Services;

/// <summary>
/// Production schema is owned by the ordered startup migrations on the Mother terminal.
/// Runtime pages and business services must never create or alter tables.
/// </summary>
public static class RuntimeSchemaPolicy
{
    // For production this should be true (migrations applied externally).
    // For local development/testing we allow the runtime to apply schema upgrades so
    // missing columns (terminal_id, enabled, websocket_*, etc.) are created automatically.
    public static bool IsMigrationManaged => false;
}

namespace POS_in_NET.Services;

/// <summary>
/// Production schema is owned by the ordered startup migrations on the Mother terminal.
/// Runtime pages and business services must never create or alter tables.
/// </summary>
public static class RuntimeSchemaPolicy
{
    public static bool IsMigrationManaged => true;
}

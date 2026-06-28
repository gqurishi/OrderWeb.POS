namespace POS_in_NET.Models;

public enum TerminalMode
{
    Mother,
    Child
}

public sealed class TerminalConfiguration
{
    public bool IsConfigured { get; init; }
    public TerminalMode Mode { get; init; } = TerminalMode.Mother;
    public string TerminalName { get; init; } = "Main";
    public string DatabaseHost { get; init; } = "localhost";
    public int DatabasePort { get; init; } = 3306;
    public string DatabaseName { get; init; } = PosDatabaseDefaults.ProductionDatabaseName;
    public string DatabaseUser { get; init; } = PosDatabaseDefaults.ProductionDatabaseUser;
    public string DatabasePassword { get; init; } = string.Empty;

    public bool IsMother => Mode == TerminalMode.Mother;
    public bool IsChild => Mode == TerminalMode.Child;
    public string ModeDisplay => IsMother ? "Mother Terminal" : "Child Terminal";
}

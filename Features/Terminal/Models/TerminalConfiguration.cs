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
    public string DatabaseName { get; init; } = "Pos-net";
    public string DatabaseUser { get; init; } = "root";
    public string DatabasePassword { get; init; } = "root";

    public bool IsMother => Mode == TerminalMode.Mother;
    public bool IsChild => Mode == TerminalMode.Child;
    public string ModeDisplay => IsMother ? "Mother Terminal" : "Child Terminal";
}

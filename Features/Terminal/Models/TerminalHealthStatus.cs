namespace POS_in_NET.Models;

public sealed class TerminalHealthStatus
{
    public string TerminalName { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string DatabaseHost { get; init; } = string.Empty;
    public DateTime LastSeenAt { get; init; }
    public string LastStatus { get; init; } = string.Empty;
    public string LastError { get; init; } = string.Empty;
    public string PairingStatus { get; init; } = string.Empty;
    public string PairingCode { get; init; } = string.Empty;
    public DateTime? PairingExpiresAt { get; init; }
    public DateTime? PairedAt { get; init; }
    public bool IsDisabled { get; init; }

    public bool IsPendingPairing => string.Equals(PairingStatus, "Pending", StringComparison.OrdinalIgnoreCase);
    public bool IsExpiredPairing => string.Equals(PairingStatus, "Expired", StringComparison.OrdinalIgnoreCase);
    public bool IsMother => string.Equals(Mode, "Mother", StringComparison.OrdinalIgnoreCase);
    public bool IsOnline => !IsPendingPairing && !IsExpiredPairing && !IsDisabled && DateTime.Now - LastSeenAt <= TimeSpan.FromSeconds(45);
    public bool CanDeleteTerminal => !IsMother && !IsOnline;
    public string StatusDisplay => IsDisabled
        ? "Disabled"
        : IsExpiredPairing
            ? "Expired"
            : IsPendingPairing
                ? "Pending"
                : IsOnline ? "Online" : "Offline";
    public string StatusColor => IsOnline
        ? "#059669"
        : IsPendingPairing
            ? "#D97706"
            : "#DC2626";
    public string ModeDisplay => string.Equals(Mode, "Mother", StringComparison.OrdinalIgnoreCase)
        ? "Mother"
        : "Child";
    public string LastSeenDisplay => IsPendingPairing || IsExpiredPairing
        ? PairingExpiresAt.HasValue ? $"Expires {PairingExpiresAt.Value:HH:mm}" : "Not paired"
        : LastSeenAt <= DateTime.MinValue.AddDays(1)
            ? "Never"
            : LastSeenAt.ToString("dd MMM yyyy HH:mm:ss");
    public string DetailDisplay => IsPendingPairing || IsExpiredPairing
        ? $"Pairing code: {PairingCode} · Connect child to {DatabaseHost}:3306"
        : string.IsNullOrWhiteSpace(LastError)
            ? $"{ModeDisplay} terminal on {DatabaseHost}"
            : LastError;
}

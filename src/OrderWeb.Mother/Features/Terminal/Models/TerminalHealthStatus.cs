namespace POS_in_NET.Models;

public sealed class TerminalHealthStatus
{
    private static readonly TimeSpan OnlineHeartbeatWindow = TimeSpan.FromSeconds(60);

    public string TerminalName { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string DatabaseHost { get; set; } = string.Empty;
    public DateTime LastSeenAt { get; set; }
    public string LastStatus { get; set; } = string.Empty;
    public string LastError { get; set; } = string.Empty;
    public string PairingStatus { get; set; } = string.Empty;
    public string PairingCode { get; set; } = string.Empty;
    public DateTime? PairingExpiresAt { get; set; }
    public DateTime? PairedAt { get; set; }
    public bool IsDisabled { get; set; }
    public string TerminalId { get; set; } = string.Empty;
    public string DeviceType { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string AppVersion { get; set; } = string.Empty;
    public string LastIpAddress { get; set; } = string.Empty;
    public long LastSyncEventId { get; set; }
    public DateTime? RevokedAt { get; set; }
    public int? CurrentUserId { get; set; }
    public string WebSocketStatus { get; set; } = string.Empty;
    public DateTime? WebSocketConnectedAt { get; set; }
    public DateTime? WebSocketDisconnectedAt { get; set; }
    public DateTime? WebSocketLastMessageAt { get; set; }

    public bool IsPendingPairing => string.Equals(PairingStatus, "Pending", StringComparison.OrdinalIgnoreCase);
    public bool IsExpiredPairing => string.Equals(PairingStatus, "Expired", StringComparison.OrdinalIgnoreCase);
    public bool IsMother => string.Equals(Mode, "Mother", StringComparison.OrdinalIgnoreCase);
    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsOnline => !IsPendingPairing && !IsExpiredPairing && !IsDisabled && !IsRevoked && DateTime.Now - LastSeenAt <= OnlineHeartbeatWindow;
    public bool CanDeleteTerminal => !IsMother && !IsOnline;
    public bool CanManageClient => !IsMother;
    public bool CanDisableTerminal => CanManageClient && !IsDisabled;
    public bool CanRevokeTerminal => CanManageClient && !IsRevoked && !string.IsNullOrWhiteSpace(TerminalId);
    public bool CanForceLogout => CanManageClient && !string.IsNullOrWhiteSpace(TerminalId);
    public bool CanEditClientAccess => CanManageClient && !string.IsNullOrWhiteSpace(TerminalId);
    public string StatusDisplay => IsDisabled
        ? "Disabled"
        : IsRevoked
            ? "Revoked"
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
        : "Client";
    public string LastSeenDisplay => IsPendingPairing || IsExpiredPairing
        ? PairingExpiresAt.HasValue ? $"Expires {PairingExpiresAt.Value:HH:mm}" : "Not paired"
        : LastSeenAt <= DateTime.MinValue.AddDays(1)
            ? "Never"
            : LastSeenAt.ToString("dd MMM yyyy HH:mm:ss");
    public string PairingDisplay => IsPendingPairing || IsExpiredPairing
        ? string.IsNullOrWhiteSpace(PairingCode) ? "No active code" : $"{PairingCode} expires {PairingExpiresAt:HH:mm}"
        : PairedAt.HasValue ? $"Paired {PairedAt.Value:dd MMM HH:mm}" : "Not paired";
    public string DeviceDisplay => string.Join(" / ", new[] { DeviceType, Platform, AppVersion }
        .Where(value => !string.IsNullOrWhiteSpace(value)));
    public string DetailDisplay => IsPendingPairing || IsExpiredPairing
        ? $"Pairing code: {PairingCode} - Connect Client to API {DatabaseHost}:5055"
        : string.IsNullOrWhiteSpace(LastError)
            ? $"{ModeDisplay} terminal on {EffectiveIpDisplay}"
            : LastError;
    public string EffectiveIpDisplay => string.IsNullOrWhiteSpace(LastIpAddress) ? DatabaseHost : LastIpAddress;
    public string DeviceDisplayOrDash => string.IsNullOrWhiteSpace(DeviceDisplay) ? "-" : DeviceDisplay;
    public string LastSyncDisplay => LastSyncEventId <= 0 ? "-" : LastSyncEventId.ToString();
    public bool IsWebSocketConnected => string.Equals(WebSocketStatus, "connected", StringComparison.OrdinalIgnoreCase);
    public string WebSocketDisplay => IsPendingPairing || IsExpiredPairing
        ? "-"
        : IsWebSocketConnected
            ? "Connected"
            : "Disconnected";
    public string WebSocketLastMessageDisplay => WebSocketLastMessageAt.HasValue
        ? $"Last message {WebSocketLastMessageAt.Value:HH:mm:ss}"
        : WebSocketConnectedAt.HasValue
            ? $"Connected {WebSocketConnectedAt.Value:HH:mm:ss}"
            : WebSocketDisconnectedAt.HasValue
                ? $"Disconnected {WebSocketDisconnectedAt.Value:HH:mm:ss}"
                : "No WebSocket yet";
}

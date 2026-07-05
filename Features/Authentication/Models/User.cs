namespace POS_in_NET.Models;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;  // Full name like "John Smith"
    public string Username { get; set; } = string.Empty;  // Short login ID like "001" or "john"
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public string StatusText => IsActive ? "Active" : "Inactive";
    public string StatusBackgroundColor => IsActive ? "#F0FDF4" : "#F8FAFC";
    public string StatusBorderColor => IsActive ? "#BBF7D0" : "#CBD5E1";
    public string StatusTextColor => IsActive ? "#15803D" : "#64748B";
    public string StatusDotColor => IsActive ? "#22C55E" : "#94A3B8";
    public string DeactivateButtonText => IsActive ? "Deactivate" : "Inactive";
    public string DeactivateButtonBackgroundColor => IsActive ? "#EF4444" : "#CBD5E1";
    public string DeactivateButtonTextColor => IsActive ? "White" : "#64748B";
    public bool CanDeactivate => IsActive;
}

public enum UserRole
{
    Staff,
    User,
    Manager,
    Admin
}

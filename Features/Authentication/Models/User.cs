namespace POS_in_NET.Models;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;  // Full name like "John Smith"
    public string Username { get; set; } = string.Empty;  // Short login ID like "001" or "john"
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum UserRole
{
    User,
    Manager,
    Admin
}
public class User
{
    public string Username { get; set; } = "";
    public string? Password { get; set; }
    public string? PasswordHash { get; set; }
    public DateTime? PasswordSetAt { get; set; }
    public bool ForcePasswordChange { get; set; } = true;
    public string Role { get; set; } = "Employee";
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string Theme { get; set; } = "light";
}
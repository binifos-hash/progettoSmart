public class LoginResultItem
{
    public string Token { get; set; } = "";
    public string Username { get; set; } = "";
    public string Role { get; set; } = "Employee";
    public string? Email { get; set; }
    public string Theme { get; set; } = "light";
    public bool ForcePasswordChange { get; set; }
}

public enum ForgotPasswordStatus
{
    NotFound,
    EmailError,
    Success
}

public enum ChangePasswordStatus
{
    InvalidInput,
    Forbidden,
    Success
}
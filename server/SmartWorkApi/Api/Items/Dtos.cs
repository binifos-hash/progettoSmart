public class LoginDto
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
}

public class ThemeDto
{
    public string Theme { get; set; } = "light";
}

public class CreateRequestDto
{
    public DateTime Date { get; set; }
}

public class CreateRecurringRequestDto
{
    public int DayOfWeek { get; set; }
    public string DayName { get; set; } = "";
}

public class ForgotDto
{
    public string Email { get; set; } = "";
}

public class ChangePasswordDto
{
    public string? OldPassword { get; set; }
    public string NewPassword { get; set; } = "";
}

public class CreateUserDto
{
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public string Email { get; set; } = "";
    public string? Role { get; set; }
}
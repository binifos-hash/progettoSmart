public class UserListItem
{
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string Role { get; set; } = "Employee";
}

public enum CreateUserStatus
{
    InvalidInput,
    AlreadyExists,
    Created
}

public class CreateUserResult
{
    public CreateUserStatus Status { get; set; }
    public User? CreatedUser { get; set; }
    public string? TemporaryPassword { get; set; }
}

public enum DeleteUserStatus
{
    NotFound,
    CannotDeleteYourself,
    CannotDeleteLastAdmin,
    Deleted
}
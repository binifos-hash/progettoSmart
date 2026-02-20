using Microsoft.EntityFrameworkCore;

public class UserDomainService
{
    private readonly EmailService _emailService;

    public UserDomainService(EmailService emailService)
    {
        _emailService = emailService;
    }

    public Task<List<UserListItem>> GetUsersAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        return db.Users
            .AsNoTracking()
            .OrderBy(u => u.Username)
            .Select(u => new UserListItem
            {
                Username = u.Username,
                DisplayName = u.DisplayName,
                Email = u.Email,
                Role = u.Role
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<CreateUserResult> CreateUserAsync(CreateUserDto dto, AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Email))
        {
            return new CreateUserResult { Status = CreateUserStatus.InvalidInput };
        }

        var exists = await db.Users.AnyAsync(u => u.Username == dto.Username, cancellationToken);
        if (exists)
        {
            return new CreateUserResult { Status = CreateUserStatus.AlreadyExists };
        }

        var tempPassword = PasswordHelper.GenerateTemporaryPassword(10);
        var user = new User
        {
            Username = dto.Username,
            DisplayName = dto.DisplayName,
            Email = dto.Email,
            Role = string.IsNullOrWhiteSpace(dto.Role) ? "Employee" : dto.Role,
            PasswordHash = PasswordHelper.HashPassword(tempPassword),
            PasswordSetAt = DateTime.UtcNow,
            ForcePasswordChange = true,
            Theme = "light"
        };

        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);

        _ = Task.Run(async () =>
        {
            try
            {
                await _emailService.SendTemporaryPasswordAsync(user.Email ?? "", user.Username, tempPassword);
            }
            catch
            {
            }
        });

        return new CreateUserResult
        {
            Status = CreateUserStatus.Created,
            CreatedUser = user,
            TemporaryPassword = tempPassword
        };
    }

    public async Task<DeleteUserStatus> DeleteUserAsync(string adminUsername, string usernameToDelete, AppDbContext db, CancellationToken cancellationToken = default)
    {
        var target = await db.Users.FirstOrDefaultAsync(u => u.Username == usernameToDelete, cancellationToken);
        if (target == null) return DeleteUserStatus.NotFound;

        if (target.Username == adminUsername) return DeleteUserStatus.CannotDeleteYourself;

        if (target.Role == "Admin")
        {
            var admins = await db.Users.CountAsync(u => u.Role == "Admin", cancellationToken);
            if (admins <= 1) return DeleteUserStatus.CannotDeleteLastAdmin;
        }

        db.Users.Remove(target);
        await db.SaveChangesAsync(cancellationToken);
        return DeleteUserStatus.Deleted;
    }

    public async Task UpdateThemeAsync(User user, string theme, AppDbContext db, CancellationToken cancellationToken = default)
    {
        user.Theme = theme;
        await db.SaveChangesAsync(cancellationToken);
    }
}
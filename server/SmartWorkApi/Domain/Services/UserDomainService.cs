using Microsoft.EntityFrameworkCore;

public class UserDomainService
{
    private readonly EmailService _emailService;
    private readonly ILogger<UserDomainService> _logger;

    public UserDomainService(EmailService emailService, ILogger<UserDomainService> logger)
    {
        _emailService = emailService;
        _logger = logger;
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
            Role = RoleHelper.Normalize(dto.Role),
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
                _logger.LogInformation("[USER] Sending onboarding temporary password email. username={Username} email={Email}", user.Username, user.Email);
                await _emailService.SendTemporaryPasswordAsync(user.Email ?? "", user.Username, tempPassword);
                _logger.LogInformation("[USER] Onboarding temporary password email finished. username={Username}", user.Username);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[USER] Failed to send onboarding temporary password email. username={Username}", user.Username);
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

        if (RoleHelper.IsAdmin(target.Role))
        {
            var admins = await db.Users.CountAsync(u => u.Role != null && u.Role.Trim().ToLower() == "admin", cancellationToken);
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
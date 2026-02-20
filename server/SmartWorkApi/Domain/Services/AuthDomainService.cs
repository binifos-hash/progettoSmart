using Microsoft.EntityFrameworkCore;

public class AuthDomainService
{
    private readonly ISessionStore _sessionStore;
    private readonly EmailService _emailService;

    public AuthDomainService(ISessionStore sessionStore, EmailService emailService)
    {
        _sessionStore = sessionStore;
        _emailService = emailService;
    }

    // Handles authentication and legacy password migration in one place.
    public async Task<LoginResultItem?> LoginAsync(LoginDto dto, AppDbContext db, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == dto.Username, cancellationToken);
        if (user == null) return null;

        var verified = false;
        if (!string.IsNullOrWhiteSpace(user.PasswordHash))
        {
            verified = PasswordHelper.VerifyPassword(user.PasswordHash, dto.Password);
        }
        else if (!string.IsNullOrEmpty(user.Password))
        {
            verified = user.Password == dto.Password;
            if (verified)
            {
                user.PasswordHash = PasswordHelper.HashPassword(dto.Password);
                user.Password = null;
                user.PasswordSetAt = DateTime.UtcNow;
                user.ForcePasswordChange = true;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        if (!verified) return null;

        if (user.PasswordSetAt == null || user.PasswordSetAt <= DateTime.UtcNow.AddMonths(-4))
        {
            user.ForcePasswordChange = true;
            await db.SaveChangesAsync(cancellationToken);
        }

        var token = _sessionStore.CreateSession(user.Username);
        return new LoginResultItem
        {
            Token = token,
            Username = user.Username,
            Role = user.Role,
            Email = user.Email,
            Theme = user.Theme,
            ForcePasswordChange = user.ForcePasswordChange
        };
    }

    public async Task<ForgotPasswordStatus> ForgotPasswordAsync(ForgotDto dto, AppDbContext db, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Email != null && u.Email.ToLower() == dto.Email.ToLower(),
            cancellationToken);

        if (user == null) return ForgotPasswordStatus.NotFound;

        var tempPassword = PasswordHelper.GenerateTemporaryPassword(10);
        user.PasswordHash = PasswordHelper.HashPassword(tempPassword);
        user.PasswordSetAt = DateTime.UtcNow;
        user.ForcePasswordChange = true;
        await db.SaveChangesAsync(cancellationToken);

        var sent = await _emailService.SendTemporaryPasswordAsync(user.Email ?? "", user.Username, tempPassword);
        return sent ? ForgotPasswordStatus.Success : ForgotPasswordStatus.EmailError;
    }

    public async Task<ChangePasswordStatus> ChangePasswordAsync(User user, ChangePasswordDto dto, AppDbContext db, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.NewPassword)) return ChangePasswordStatus.InvalidInput;

        if (!user.ForcePasswordChange)
        {
            if (string.IsNullOrWhiteSpace(dto.OldPassword)) return ChangePasswordStatus.InvalidInput;

            var validOldPassword = !string.IsNullOrWhiteSpace(user.PasswordHash)
                ? PasswordHelper.VerifyPassword(user.PasswordHash, dto.OldPassword)
                : !string.IsNullOrEmpty(user.Password) && user.Password == dto.OldPassword;

            if (!validOldPassword) return ChangePasswordStatus.Forbidden;
        }

        user.PasswordHash = PasswordHelper.HashPassword(dto.NewPassword);
        user.PasswordSetAt = DateTime.UtcNow;
        user.ForcePasswordChange = false;
        user.Password = null;

        await db.SaveChangesAsync(cancellationToken);
        return ChangePasswordStatus.Success;
    }
}
using System.Data;
using Microsoft.EntityFrameworkCore;

public class RecurringRequestDomainService
{
    private readonly EmailService _emailService;

    public RecurringRequestDomainService(EmailService emailService)
    {
        _emailService = emailService;
    }

    public Task<List<RecurringRequest>> GetAllAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        return db.RecurringRequests
            .AsNoTracking()
            .OrderBy(r => r.DayOfWeek)
            .ToListAsync(cancellationToken);
    }

    public Task<List<RecurringRequest>> GetMineAsync(string username, AppDbContext db, CancellationToken cancellationToken = default)
    {
        return db.RecurringRequests
            .AsNoTracking()
            .Where(r => r.EmployeeUsername == username)
            .OrderBy(r => r.DayOfWeek)
            .ToListAsync(cancellationToken);
    }

    public async Task<RecurringRequest> CreateAsync(User user, CreateRecurringRequestDto dto, AppDbContext db, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var maxRequestId = await db.Requests.Select(x => (int?)x.Id).MaxAsync(cancellationToken) ?? 0;
        var maxRecurringId = await db.RecurringRequests.Select(x => (int?)x.Id).MaxAsync(cancellationToken) ?? 0;
        var nextId = Math.Max(maxRequestId, maxRecurringId) + 1;

        var request = new RecurringRequest
        {
            Id = nextId,
            EmployeeUsername = user.Username,
            EmployeeName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
            DayOfWeek = dto.DayOfWeek,
            DayName = dto.DayName,
            Status = "Pending"
        };

        db.RecurringRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _ = Task.Run(async () =>
        {
            await _emailService.SendRequestNotificationAsync("paolo.bini@fos.it", request.EmployeeName, $"ogni {request.DayName}");
        });

        return request;
    }

    public async Task<RecurringRequest?> SetDecisionAsync(int id, bool approved, string adminUsername, AppDbContext db, CancellationToken cancellationToken = default)
    {
        var request = await db.RecurringRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request == null) return null;

        var employeeEmail = await db.Users
            .AsNoTracking()
            .Where(u => u.Username == request.EmployeeUsername)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken);

        request.Status = approved ? "Approved" : "Rejected";
        request.DecisionBy = adminUsername;
        request.DecisionAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        _ = Task.Run(async () =>
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(employeeEmail))
                {
                    await _emailService.SendDecisionNotificationAsync(
                        employeeEmail,
                        request.EmployeeName,
                        $"ogni {request.DayName}",
                        approved,
                        adminUsername);
                }
            }
            catch
            {
            }
        });

        return request;
    }

    public async Task<EntityDeleteStatus> DeleteAsync(User user, int id, AppDbContext db, CancellationToken cancellationToken = default)
    {
        var request = await db.RecurringRequests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request == null) return EntityDeleteStatus.NotFound;
        if (request.EmployeeUsername != user.Username && user.Role != "Admin") return EntityDeleteStatus.Forbidden;

        db.RecurringRequests.Remove(request);
        await db.SaveChangesAsync(cancellationToken);
        return EntityDeleteStatus.Deleted;
    }
}
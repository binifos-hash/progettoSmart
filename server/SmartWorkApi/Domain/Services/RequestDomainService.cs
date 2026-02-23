using System.Data;
using Microsoft.EntityFrameworkCore;

public class RequestDomainService
{
    private readonly EmailService _emailService;
    private readonly ILogger<RequestDomainService> _logger;

    public RequestDomainService(EmailService emailService, ILogger<RequestDomainService> logger)
    {
        _emailService = emailService;
        _logger = logger;
    }

    public Task<List<Request>> GetAllAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        return db.Requests
            .AsNoTracking()
            .OrderBy(r => r.Date)
            .ToListAsync(cancellationToken);
    }

    public Task<List<Request>> GetMineAsync(string username, AppDbContext db, CancellationToken cancellationToken = default)
    {
        return db.Requests
            .AsNoTracking()
            .Where(r => r.EmployeeUsername == username)
            .OrderBy(r => r.Date)
            .ToListAsync(cancellationToken);
    }

    // Uses a serializable transaction to keep ID generation safe under concurrency.
    public async Task<Request> CreateAsync(User user, CreateRequestDto dto, AppDbContext db, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var maxRequestId = await db.Requests.Select(x => (int?)x.Id).MaxAsync(cancellationToken) ?? 0;
        var maxRecurringId = await db.RecurringRequests.Select(x => (int?)x.Id).MaxAsync(cancellationToken) ?? 0;
        var nextId = Math.Max(maxRequestId, maxRecurringId) + 1;

        var request = new Request
        {
            Id = nextId,
            EmployeeUsername = user.Username,
            EmployeeName = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName,
            Date = EnsureUtcDate(dto.Date),
            Status = "Pending"
        };

        db.Requests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _ = Task.Run(async () =>
        {
            try
            {
                _logger.LogInformation("[REQUEST] Sending new request notification. requestId={RequestId} employee={EmployeeName} date={Date}", request.Id, request.EmployeeName, request.Date.ToString("yyyy-MM-dd"));
                var sent = await _emailService.SendRequestNotificationAsync("paolo.bini@fos.it", request.EmployeeName, request.Date.ToString("yyyy-MM-dd"));
                if (sent)
                {
                    _logger.LogInformation("[REQUEST] New request notification sent. requestId={RequestId}", request.Id);
                }
                else
                {
                    _logger.LogWarning("[REQUEST] New request notification failed. requestId={RequestId}", request.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[REQUEST] Failed to send new request notification. requestId={RequestId}", request.Id);
            }
        });

        return request;
    }

    // PostgreSQL timestamptz requires UTC DateTime values.
    private static DateTime EnsureUtcDate(DateTime value)
    {
        return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    }

    public async Task<Request?> SetDecisionAsync(int id, bool approved, string adminUsername, AppDbContext db, CancellationToken cancellationToken = default)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
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
                    _logger.LogInformation("[REQUEST] Sending decision notification. requestId={RequestId} approved={Approved} employeeEmail={EmployeeEmail}", request.Id, approved, employeeEmail);
                    var sent = await _emailService.SendDecisionNotificationAsync(
                        employeeEmail,
                        request.EmployeeName,
                        request.Date.ToString("yyyy-MM-dd"),
                        approved,
                        adminUsername);

                    if (sent)
                    {
                        _logger.LogInformation("[REQUEST] Decision notification sent. requestId={RequestId}", request.Id);
                    }
                    else
                    {
                        _logger.LogWarning("[REQUEST] Decision notification failed. requestId={RequestId}", request.Id);
                    }
                }
                else
                {
                    _logger.LogWarning("[REQUEST] Decision notification skipped: missing employee email. requestId={RequestId}", request.Id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[REQUEST] Failed to send decision notification. requestId={RequestId}", request.Id);
            }
        });

        return request;
    }

    public async Task<EntityDeleteStatus> DeleteAsync(User user, int id, AppDbContext db, CancellationToken cancellationToken = default)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request == null) return EntityDeleteStatus.NotFound;
        if (request.EmployeeUsername != user.Username && user.Role != "Admin") return EntityDeleteStatus.Forbidden;

        db.Requests.Remove(request);
        await db.SaveChangesAsync(cancellationToken);
        return EntityDeleteStatus.Deleted;
    }
}
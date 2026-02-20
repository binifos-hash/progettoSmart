public static class RecurringRequestEndpoints
{
    public static IEndpointRouteBuilder MapRecurringRequestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/recurring-requests", async (HttpRequest req, AppDbContext db, ICurrentUserService currentUserService, RecurringRequestDomainService recurringDomain, CancellationToken ct) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (user == null || user.Role != "Admin") return Results.Unauthorized();

            return Results.Ok(await recurringDomain.GetAllAsync(db, ct));
        });

        app.MapGet("/recurring-requests/mine", async (HttpRequest req, AppDbContext db, ICurrentUserService currentUserService, RecurringRequestDomainService recurringDomain, CancellationToken ct) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (user == null) return Results.Unauthorized();

            return Results.Ok(await recurringDomain.GetMineAsync(user.Username, db, ct));
        });

        app.MapPost("/recurring-requests", async (HttpRequest req, CreateRecurringRequestDto dto, AppDbContext db, ICurrentUserService currentUserService, RecurringRequestDomainService recurringDomain, CancellationToken ct) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (user == null) return Results.Unauthorized();
            if (dto == null || dto.DayOfWeek < 0 || dto.DayOfWeek > 6) return Results.BadRequest();

            var created = await recurringDomain.CreateAsync(user, dto, db, ct);
            return Results.Created($"/recurring-requests/{created.Id}", created);
        });

        app.MapPost("/recurring-requests/{id}/approve", async (HttpRequest req, int id, AppDbContext db, ICurrentUserService currentUserService, RecurringRequestDomainService recurringDomain, CancellationToken ct) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (user == null || user.Role != "Admin") return Results.Unauthorized();

            var updated = await recurringDomain.SetDecisionAsync(id, true, user.Username, db, ct);
            return updated == null ? Results.NotFound() : Results.Ok(updated);
        });

        app.MapPost("/recurring-requests/{id}/reject", async (HttpRequest req, int id, AppDbContext db, ICurrentUserService currentUserService, RecurringRequestDomainService recurringDomain, CancellationToken ct) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (user == null || user.Role != "Admin") return Results.Unauthorized();

            var updated = await recurringDomain.SetDecisionAsync(id, false, user.Username, db, ct);
            return updated == null ? Results.NotFound() : Results.Ok(updated);
        });

        app.MapDelete("/recurring-requests/{id}", async (HttpRequest req, int id, AppDbContext db, ICurrentUserService currentUserService, RecurringRequestDomainService recurringDomain, CancellationToken ct) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (user == null) return Results.Unauthorized();

            var status = await recurringDomain.DeleteAsync(user, id, db, ct);
            return status switch
            {
                EntityDeleteStatus.NotFound => Results.NotFound(),
                EntityDeleteStatus.Forbidden => Results.Forbid(),
                _ => Results.Ok(new { message = "Deleted" })
            };
        });

        return app;
    }
}
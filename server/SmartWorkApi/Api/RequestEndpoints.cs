public static class RequestEndpoints
{
    public static IEndpointRouteBuilder MapRequestEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/requests", async (HttpRequest req, AppDbContext db, ICurrentUserService currentUserService, RequestDomainService requestDomain, CancellationToken ct) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (user == null || !RoleHelper.IsAdmin(user.Role)) return Results.Unauthorized();

            return Results.Ok(await requestDomain.GetAllAsync(db, ct));
        });

        app.MapGet("/requests/mine", async (HttpRequest req, AppDbContext db, ICurrentUserService currentUserService, RequestDomainService requestDomain, CancellationToken ct) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (user == null) return Results.Unauthorized();

            return Results.Ok(await requestDomain.GetMineAsync(user.Username, db, ct));
        });

        app.MapPost("/requests", async (HttpRequest req, CreateRequestDto dto, AppDbContext db, ICurrentUserService currentUserService, RequestDomainService requestDomain, CancellationToken ct) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (user == null) return Results.Unauthorized();
            if (dto == null) return Results.BadRequest();

            var created = await requestDomain.CreateAsync(user, dto, db, ct);
            return Results.Created($"/requests/{created.Id}", created);
        });

        app.MapPost("/requests/{id}/approve", async (HttpRequest req, int id, AppDbContext db, ICurrentUserService currentUserService, RequestDomainService requestDomain, CancellationToken ct) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (user == null || !RoleHelper.IsAdmin(user.Role)) return Results.Unauthorized();

            var updated = await requestDomain.SetDecisionAsync(id, true, user.Username, db, ct);
            return updated == null ? Results.NotFound() : Results.Ok(updated);
        });

        app.MapPost("/requests/{id}/reject", async (HttpRequest req, int id, AppDbContext db, ICurrentUserService currentUserService, RequestDomainService requestDomain, CancellationToken ct) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (user == null || !RoleHelper.IsAdmin(user.Role)) return Results.Unauthorized();

            var updated = await requestDomain.SetDecisionAsync(id, false, user.Username, db, ct);
            return updated == null ? Results.NotFound() : Results.Ok(updated);
        });

        app.MapDelete("/requests/{id}", async (HttpRequest req, int id, AppDbContext db, ICurrentUserService currentUserService, RequestDomainService requestDomain, CancellationToken ct) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (user == null) return Results.Unauthorized();

            var status = await requestDomain.DeleteAsync(user, id, db, ct);
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
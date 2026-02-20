public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/me/change-password", async (HttpRequest req, ChangePasswordDto dto, AppDbContext db, ICurrentUserService currentUserService, AuthDomainService authDomain, CancellationToken ct) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (user == null) return Results.Unauthorized();
            if (dto == null) return Results.BadRequest();

            var result = await authDomain.ChangePasswordAsync(user, dto, db, ct);
            return result switch
            {
                ChangePasswordStatus.InvalidInput => Results.BadRequest(),
                ChangePasswordStatus.Forbidden => Results.Forbid(),
                _ => Results.Ok(new { message = "Password changed" })
            };
        });

        app.MapGet("/me", async (HttpRequest req, AppDbContext db, ICurrentUserService currentUserService, CancellationToken ct) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (user == null) return Results.Unauthorized();
            return Results.Ok(new { username = user.Username, role = user.Role, email = user.Email, theme = user.Theme });
        });

        app.MapPost("/me/theme", async (HttpRequest req, ThemeDto dto, AppDbContext db, ICurrentUserService currentUserService, UserDomainService userDomain, CancellationToken ct) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (user == null) return Results.Unauthorized();
            if (dto == null || string.IsNullOrWhiteSpace(dto.Theme)) return Results.BadRequest();

            await userDomain.UpdateThemeAsync(user, dto.Theme, db, ct);
            return Results.Ok(new { theme = user.Theme });
        });

        return app;
    }
}
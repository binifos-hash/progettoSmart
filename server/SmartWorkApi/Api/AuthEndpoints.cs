public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", async (LoginDto dto, AppDbContext db, AuthDomainService authDomain, CancellationToken ct) =>
        {
            if (dto == null) return Results.BadRequest();

            var result = await authDomain.LoginAsync(dto, db, ct);
            return result == null ? Results.Unauthorized() : Results.Ok(new
            {
                token = result.Token,
                username = result.Username,
                role = result.Role,
                email = result.Email,
                theme = result.Theme,
                forcePasswordChange = result.ForcePasswordChange
            });
        });

        app.MapPost("/auth/forgot-password", async (ForgotDto dto, AppDbContext db, AuthDomainService authDomain, CancellationToken ct) =>
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Email)) return Results.BadRequest();

            var status = await authDomain.ForgotPasswordAsync(dto, db, ct);
            return status switch
            {
                ForgotPasswordStatus.NotFound => Results.NotFound(),
                ForgotPasswordStatus.EmailError => Results.Problem("Failed to send temporary password email. Check SMTP settings/logs.", statusCode: 500),
                _ => Results.Ok(new { message = "Temporary password sent" })
            };
        });

        return app;
    }
}
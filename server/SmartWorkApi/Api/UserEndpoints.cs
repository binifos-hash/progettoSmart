public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/users", async (HttpRequest req, AppDbContext db, ICurrentUserService currentUserService, UserDomainService userDomain, CancellationToken ct) =>
        {
            var user = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (user == null || user.Role != "Admin") return Results.Unauthorized();

            var users = await userDomain.GetUsersAsync(db, ct);
            return Results.Ok(users.Select(u => new { username = u.Username, displayName = u.DisplayName, email = u.Email, role = u.Role }));
        });

        app.MapPost("/users", async (HttpRequest req, CreateUserDto dto, AppDbContext db, ICurrentUserService currentUserService, UserDomainService userDomain, CancellationToken ct) =>
        {
            var admin = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (admin == null || admin.Role != "Admin") return Results.Unauthorized();

            var result = await userDomain.CreateUserAsync(dto, db, ct);
            return result.Status switch
            {
                CreateUserStatus.InvalidInput => Results.BadRequest(new { error = "Username and email are required" }),
                CreateUserStatus.AlreadyExists => Results.Conflict(new { error = "Username already exists" }),
                _ => Results.Created($"/users/{result.CreatedUser!.Username}", new
                {
                    username = result.CreatedUser.Username,
                    displayName = result.CreatedUser.DisplayName,
                    email = result.CreatedUser.Email,
                    role = result.CreatedUser.Role
                })
            };
        });

        app.MapDelete("/users/{username}", async (HttpRequest req, string username, AppDbContext db, ICurrentUserService currentUserService, UserDomainService userDomain, CancellationToken ct) =>
        {
            var admin = await currentUserService.GetCurrentUserAsync(req, db, ct);
            if (admin == null || admin.Role != "Admin") return Results.Unauthorized();

            var result = await userDomain.DeleteUserAsync(admin.Username, username, db, ct);
            return result switch
            {
                DeleteUserStatus.NotFound => Results.NotFound(),
                DeleteUserStatus.CannotDeleteYourself => Results.BadRequest(new { error = "Cannot delete yourself" }),
                DeleteUserStatus.CannotDeleteLastAdmin => Results.BadRequest(new { error = "Cannot delete the last admin" }),
                _ => Results.Ok(new { message = "Deleted", username })
            };
        });

        return app;
    }
}
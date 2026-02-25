public static class RoleHelper
{
    public const string Admin = "Admin";
    public const string Employee = "Employee";

    public static bool IsAdmin(string? role)
    {
        return string.Equals(Normalize(role), Admin, StringComparison.Ordinal);
    }

    public static string Normalize(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return Employee;

        var value = role.Trim();
        if (value.Equals(Admin, StringComparison.OrdinalIgnoreCase)) return Admin;
        if (value.Equals(Employee, StringComparison.OrdinalIgnoreCase)) return Employee;

        return value;
    }
}
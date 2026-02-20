public class RecurringRequest
{
    public int Id { get; set; }
    public string EmployeeUsername { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public int DayOfWeek { get; set; }
    public string DayName { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public string? DecisionBy { get; set; }
    public DateTime? DecisionAt { get; set; }
}
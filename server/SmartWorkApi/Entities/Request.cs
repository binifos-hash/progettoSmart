public class Request
{
    public int Id { get; set; }
    public string EmployeeUsername { get; set; } = "";
    public string EmployeeName { get; set; } = "";
    public DateTime Date { get; set; }
    public string Status { get; set; } = "Pending";
    public string? DecisionBy { get; set; }
    public DateTime? DecisionAt { get; set; }
}
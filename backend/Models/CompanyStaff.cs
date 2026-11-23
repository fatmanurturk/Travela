namespace Travela.Models;

public class CompanyStaff
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string FullName { get; set; } = "";
    public string Duty { get; set; } = "Driver";
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TripCrew
{
    public int TripId { get; set; }
    public int StaffId { get; set; }
    public int CompanyId { get; set; }
    public string Duty { get; set; } = "Driver";
    public CompanyStaff Staff { get; set; } = null!;
    public Travela.Api.Models.Trip Trip { get; set; } = null!;
}

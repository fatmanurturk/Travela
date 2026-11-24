namespace Travela.Models;

public class CompanyStaff
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string FullName { get; set; } = "";
    public string Duty { get; set; } = "Driver";
    public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
    public int? CareerStartYear { get; set; }
    public DateOnly? EmploymentStartDate { get; set; }
    public string? EmergencyContactName { get; set; }
    public string? EmergencyContactRelationship { get; set; }
    public string? EmergencyContactPhone { get; set; }
    public string[] Languages { get; set; } = [];
    public string[] LicenseClasses { get; set; } = [];
    public string[] VehicleTypes { get; set; } = [];
    public bool ServiceExperience { get; set; }
    public string? ServiceExperienceNotes { get; set; }
    public bool BaggageExperience { get; set; }
    public string? BaggageExperienceNotes { get; set; }
    public bool PassengerCommunicationExperience { get; set; }
    public string? PassengerCommunicationExperienceNotes { get; set; }
}

// Private binary storage: metadata and bytes commit/roll back together. Never served by static middleware.
public class StaffPhoto
{
    public int StaffId { get; set; }
    public int CompanyId { get; set; }
    public string ContentType { get; set; } = "";
    public byte[] Data { get; set; } = [];
}

public class StaffDocument
{
    public int Id { get; set; }
    public int StaffId { get; set; }
    public int CompanyId { get; set; }
    public CompanyStaff Staff { get; set; } = null!;
    public string Name { get; set; } = "";
    public string DocumentType { get; set; } = "Other";
    public DateOnly IssuedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public bool IsIndefinite { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public int Size { get; set; }
    public byte[] Data { get; set; } = [];
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

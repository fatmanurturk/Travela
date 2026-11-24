using System.ComponentModel.DataAnnotations;

namespace Travela.DTOs;

public class StaffInputDto
{
    [Required, MinLength(2), MaxLength(120)] public string FullName { get; set; } = "";
    [Required, RegularExpression("^(Driver|Attendant)$")] public string Duty { get; set; } = "Driver";
    [MaxLength(32)] public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
}

public class StaffProfileInputDto : StaffInputDto
{
    [Range(1900, 9999)] public int? CareerStartYear { get; set; }
    public DateOnly? EmploymentStartDate { get; set; }
    [MaxLength(120)] public string? EmergencyContactName { get; set; }
    [MaxLength(80)] public string? EmergencyContactRelationship { get; set; }
    [MaxLength(32)] public string? EmergencyContactPhone { get; set; }
    [Required, MaxLength(30)] public string[] Languages { get; set; } = [];
    [Required, MaxLength(30)] public string[] LicenseClasses { get; set; } = [];
    [Required, MaxLength(30)] public string[] VehicleTypes { get; set; } = [];
    public bool ServiceExperience { get; set; }
    [MaxLength(1000)] public string? ServiceExperienceNotes { get; set; }
    public bool BaggageExperience { get; set; }
    [MaxLength(1000)] public string? BaggageExperienceNotes { get; set; }
    public bool PassengerCommunicationExperience { get; set; }
    [MaxLength(1000)] public string? PassengerCommunicationExperienceNotes { get; set; }
}

public class StaffDocumentInputDto
{
    [Required, MaxLength(160)] public string Name { get; set; } = "";
    [Required, RegularExpression("^(DrivingLicense|Professional|FirstAid|Training|Other)$")]
    public string DocumentType { get; set; } = "Other";
    [Required] public DateOnly? IssuedOn { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public bool IsIndefinite { get; set; }
    public IFormFile? File { get; set; }
}

public class CrewSelectionDto
{
    [Required, MaxLength(30)] public List<int> Drivers { get; set; } = [];
    [Required, MaxLength(30)] public List<int> Attendants { get; set; } = [];
}

public class SaveCrewDto
{
    [Required] public CrewSelectionDto Crew { get; set; } = new();
    public int ExpectedVersion { get; set; }
}

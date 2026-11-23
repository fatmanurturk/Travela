using System.ComponentModel.DataAnnotations;

namespace Travela.DTOs;

public class StaffInputDto
{
    [Required, MinLength(2), MaxLength(120)] public string FullName { get; set; } = "";
    [Required, RegularExpression("^(Driver|Attendant)$")] public string Duty { get; set; } = "Driver";
    [MaxLength(32)] public string? Phone { get; set; }
    public bool IsActive { get; set; } = true;
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

using System.ComponentModel.DataAnnotations;

namespace Travela.DTOs;

public class LegalAcceptanceDto
{
    [Required, MaxLength(40)]
    public string DocumentType { get; set; } = "";

    [Required, MaxLength(40)]
    public string Version { get; set; } = "";

    public bool Accepted { get; set; }
}

public class AcceptPurchaseLegalDto
{
    [MaxLength(8)]
    public List<LegalAcceptanceDto>? LegalAcceptances { get; set; }
}

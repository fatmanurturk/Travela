using System.ComponentModel.DataAnnotations;

namespace Travela.API.DTOs
{
    public class CreateCompanyDto
    {
        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [EmailAddress]
        public string? Email { get; set; }

        public string? Phone { get; set; }

        public string? Address { get; set; }
    }
}
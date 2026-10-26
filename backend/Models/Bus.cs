namespace Travela.Api.Models
{
    public class Bus
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }

        public string PlateNumber { get; set; } = string.Empty;

        public string NormalizedPlateNumber { get; set; } = string.Empty;

        public string? Brand { get; set; }

        public string? Model { get; set; }

        public int SeatCapacity { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public Company? Company { get; set; }
    }
}
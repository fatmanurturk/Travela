namespace Travela.DTOs
{
    public class CreateBusDto
    {
        public int CompanyId { get; set; }

        public string PlateNumber { get; set; } = string.Empty;

        public string? Brand { get; set; }

        public string? Model { get; set; }

        public int SeatCapacity { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
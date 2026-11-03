namespace Travela.Models
{
    public class User
    {
        public int Id { get; set; }

        public string FullName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        [System.ComponentModel.DataAnnotations.MaxLength(20)]
        public string? PhoneNumber { get; set; }
        public bool TripRemindersEnabled { get; set; }
        public bool CancellationNotificationsEnabled { get; set; }
        public bool RefundNotificationsEnabled { get; set; }

        public string PasswordHash { get; set; } = string.Empty;

        public string Role { get; set; } = "Passenger";

        public int? CompanyId { get; set; }

        public bool IsActive { get; set; } = true;

        public bool MustChangePassword { get; set; } = false;

        public int SecurityVersion { get; set; } = 1;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public DateTime? LastLoginAt { get; set; }

        public string? PasswordResetTokenHash { get; set; }

        public DateTime? PasswordResetTokenExpiresAt { get; set; }

        public DateTime? PasswordResetTokenUsedAt { get; set; }
    }
}
using System.ComponentModel.DataAnnotations;

namespace Travela.DTOs
{
    public class RegisterDto
    {
        [Required(ErrorMessage = "Ad soyad zorunludur.")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "E-posta zorunludur.")]
        [EmailAddress(ErrorMessage = "Geçerli bir e-posta giriniz.")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Şifre zorunludur.")]
        [MinLength(8, ErrorMessage = "Şifre en az 8 karakter olmalıdır.")]
        public string Password { get; set; } = string.Empty;
    }
}
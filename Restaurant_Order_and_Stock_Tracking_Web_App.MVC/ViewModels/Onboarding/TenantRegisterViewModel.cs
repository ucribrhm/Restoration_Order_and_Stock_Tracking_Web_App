using System.ComponentModel.DataAnnotations;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Onboarding;

public class TenantRegisterViewModel
{
    [Required(ErrorMessage = "Restoran adı zorunludur.")]
    [StringLength(100, ErrorMessage = "Restoran adı en fazla 100 karakter olabilir.")]
    [Display(Name = "Restoran Adı")]
    public string RestaurantName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Subdomain zorunludur.")]
    [RegularExpression(@"^[a-z0-9\-]+$", ErrorMessage = "Yalnızca küçük harf, rakam ve tire kullanılabilir.")]
    [StringLength(50, ErrorMessage = "Subdomain en fazla 50 karakter olabilir.")]
    [Display(Name = "Subdomain (örn: burger-palace)")]
    public string Subdomain { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ad Soyad zorunludur.")]
    [Display(Name = "Ad Soyad")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Kullanıcı adı zorunludur.")]
    [Display(Name = "Kullanıcı Adı")]
    public string Username { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    [Display(Name = "E-posta")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "Şifre zorunludur.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Şifre en az 8 karakter olmalıdır.")]
    [DataType(DataType.Password)]
    [Display(Name = "Şifre")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre tekrar zorunludur.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "Şifreler uyuşmuyor.")]
    [Display(Name = "Şifre Tekrar")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
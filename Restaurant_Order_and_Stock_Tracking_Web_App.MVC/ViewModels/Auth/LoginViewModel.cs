// ════════════════════════════════════════════════════════════════════════════
//  ViewModels/Auth/LoginViewModel.cs
//  Yol: ViewModels/Auth/LoginViewModel.cs
//
//  SPRINT C — [SC-1] Workspace Login ViewModel
//
//  FirmaKodu alanı eklendi.
//  Giriş formu artık üç alan içeriyor:
//    1. FirmaKodu  → Tenant slug'ı (ör: "burger-palace-a1b2c3d4")
//    2. Username   → Kısa kullanıcı adı, prefix'siz (ör: "ahmet")
//    3. Password   → Şifre
//
//  AuthController.Login POST bu üç alanı alır;
//  fullUsername = $"{FirmaKodu}_{Username}" olarak birleştirir.
// ════════════════════════════════════════════════════════════════════════════

using System.ComponentModel.DataAnnotations;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Auth;

public class LoginViewModel
{
    // ── [SC-1] Firma Kodu — Workspace Login'in birinci adımı ─────────────
    // Kullanıcının hangi restorana ait olduğunu belirler.
    // DB'de Tenant.TenantId ile eşleşir (ör: "burger-palace-a1b2c3d4").
    [Required(ErrorMessage = "Firma kodu zorunludur.")]
    [Display(Name = "Firma Kodu")]
    public string FirmaKodu { get; set; } = string.Empty;

    // ── Kısa Kullanıcı Adı — prefix'siz ─────────────────────────────────
    // Kullanıcı "ahmet" girer; controller arka planda "burger-palace-a1b2c3d4_ahmet"
    // olarak birleştirir ve Identity'de tam username olarak arar.
    [Required(ErrorMessage = "Kullanıcı adı zorunludur.")]
    [Display(Name = "Kullanıcı Adı")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre zorunludur.")]
    [DataType(DataType.Password)]
    [Display(Name = "Şifre")]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Beni Hatırla")]
    public bool RememberMe { get; set; }
}
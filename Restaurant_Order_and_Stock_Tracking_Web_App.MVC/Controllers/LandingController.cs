// ════════════════════════════════════════════════════════════════════════════
//  Controllers/LandingController.cs
//  Yol: Controllers/LandingController.cs
//
//  SPRINT B — [SB-6] Onboarding UX — Firma Kodu Gösterimi
//
//  Register POST başarıda artık Login'e değil Success action'a yönlendiriyor.
//  TempData["FirmaKodu"] → üretilen tenantId (ör: "burger-palace-a1b2c3d4")
//  TempData["RestaurantName"] → restoranın görünen adı
//  TempData["AdminUsername"]  → kısa admin kullanıcı adı (prefix'siz)
//
//  Success GET: TempData boşsa (sayfa yenilenmiş) Register'a yönlendirir.
//  Böylece kullanıcı Success sayfasını bookmark'layıp tekrar açamaz.
// ════════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Onboarding;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Controllers;

[AllowAnonymous]
public class LandingController : Controller
{
    private readonly ITenantOnboardingService _onboardingService;

    public LandingController(ITenantOnboardingService onboardingService)
    {
        _onboardingService = onboardingService;
    }

    // ── GET / ─────────────────────────────────────────────────────────────
    public IActionResult Index()
    {
        ViewData["Title"] = "RestaurantOS — Restoranınızı Dijitalleştirin";
        return View();
    }

    // ── GET /Landing/Register ──────────────────────────────────────────────
    public IActionResult Register()
    {
        ViewData["Title"] = "Restoranınızı Kaydedin";
        return View();
    }

    // ── POST /Landing/Register ─────────────────────────────────────────────
    // [SB-6] Başarılı kayıt → Success action'a yönlendir.
    //        TempData ile Firma Kodu, restoran adı ve admin adı taşınır.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(TenantRegisterViewModel model)
    {
        ViewData["Title"] = "Restoranınızı Kaydedin";

        if (!ModelState.IsValid)
            return View(model);

        var dto = new TenantOnboardingDto(
            RestaurantName: model.RestaurantName,
            Subdomain: model.Subdomain,
            AdminUsername: model.Username,
            Password: model.Password,
            FullName: model.FullName,
            Email: model.Email
        );

        var (success, tenantId, error) = await _onboardingService.CreateTenantAsync(dto);

        if (!success)
        {
            ModelState.AddModelError(string.Empty, error ?? "Kayıt sırasında bir hata oluştu.");
            return View(model);
        }

        // [SB-6] Firma Kodunu ve ilgili bilgileri TempData'ya yaz.
        // PRG (Post-Redirect-Get) deseni: yönlendirmeden önce TempData'ya,
        // Success GET'te TempData'dan oku.
        TempData["FirmaKodu"] = tenantId;
        TempData["RestaurantName"] = model.RestaurantName;
        TempData["AdminUsername"] = model.Username;   // kısa ad (prefix'siz)

        return RedirectToAction(nameof(Success));
    }

    // ── GET /Landing/Success ───────────────────────────────────────────────
    // [SB-6] Kayıt başarı sayfası.
    //        TempData boşsa (sayfa refresh, doğrudan URL girişi) Register'a dön.
    public IActionResult Success()
    {
        // TempData["FirmaKodu"] yoksa kullanıcı sayfayı yenilemiş demektir.
        // Güvenli yönlendirme: Register sayfasına gönder.
        if (TempData["FirmaKodu"] is not string firmaKodu || string.IsNullOrEmpty(firmaKodu))
            return RedirectToAction(nameof(Register));

        ViewData["Title"] = "Restoranınız Oluşturuldu 🎉";
        ViewBag.FirmaKodu = firmaKodu;
        ViewBag.RestaurantName = TempData["RestaurantName"] as string ?? "";
        ViewBag.AdminUsername = TempData["AdminUsername"] as string ?? "";

        return View();
    }
}
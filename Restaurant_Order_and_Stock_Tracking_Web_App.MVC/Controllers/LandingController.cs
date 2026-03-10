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

    // ── GET / (Ana sayfa / Landing) ───────────────────────────────────
    public IActionResult Index()
    {
        ViewData["Title"] = "RestaurantOS — Restoranınızı Dijitalleştirin";
        return View();
    }

    // ── GET /Landing/Register ─────────────────────────────────────────
    public IActionResult Register()
    {
        ViewData["Title"] = "Restoranınızı Kaydedin";
        return View();
    }

    // ── POST /Landing/Register ────────────────────────────────────────
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

        TempData["Success"] = "Restoranınız başarıyla oluşturuldu! Aşağıdan giriş yapabilirsiniz.";
        return RedirectToAction("Login", "Auth", new { area = "App" });
    }
}
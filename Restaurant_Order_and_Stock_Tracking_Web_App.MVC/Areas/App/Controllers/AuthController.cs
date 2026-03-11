// ════════════════════════════════════════════════════════════════════════════
//  Areas/App/Controllers/AuthController.cs
//  Yol: Areas/App/Controllers/AuthController.cs
//
//  SPRINT A — [SA-4] Trial kontrolü (korundu)
//
//  SPRINT C — [SC-2] Workspace Login Akışı
//
//  Login POST artık üç aşamalı Workspace Login akışını uygular:
//
//  AŞAMA 1 — Tenant Doğrulama + Timing Attack Koruması
//    model.FirmaKodu ile DB'de aktif ve süresi dolmamış tenant aranır.
//    Tenant bulunamazsa:
//      → Task.Delay(100–300 ms) yapay gecikme eklenir.
//      → Genel hata mesajı: "Firma kodu, kullanıcı adı veya şifre hatalı."
//        (Saldırgan firma kodunun yanlış olduğunu öğrenemez.)
//
//  AŞAMA 2 — Kullanıcı Adı Birleştirme
//    fullUsername = $"{model.FirmaKodu}_{model.Username}"
//    Bu değer Identity'nin UserName sütunundaki tam değerle eşleşir.
//
//  AŞAMA 3 — Standart Doğrulama
//    Lockout, şifre, SysAdmin filtresi, AppAuth cookie — öncekiyle aynı.
//    Tenant kontrolü artık AŞAMA 1'de yapıldığı için tekrarlanmıyor.
// ════════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Auth;
using System.Security.Claims;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.App.Controllers;

[Area("App")]
public class AuthController : AppBaseController
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RestaurantDbContext _db;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        RestaurantDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    // ── GET /App/Auth/Login ────────────────────────────────────────────────
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Tables", new { area = "App" });

        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    // ── POST /App/Auth/Login ───────────────────────────────────────────────
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
            return View(model);

        // ════════════════════════════════════════════════════════════════
        //  AŞAMA 1 — Tenant Doğrulama + Timing Attack Koruması
        //
        //  FirmaKodu → DB'de aktif ve süresi dolmamış tenant aranır.
        //
        //  Neden önce tenant, sonra kullanıcı?
        //    • Kullanıcıyı önce aramak: saldırgana "bu kullanıcı bu tenant'ta
        //      var mı?" bilgisini timing farkı ile sızdırır.
        //    • Tenant kontrolünü önce yapıp başarısızlıkta aynı genel mesajı
        //      vermek + yapay gecikme eklemek bu farkı kapatır.
        //
        //  Başarısızlık mesajı kasıtlı olarak geneldir:
        //    "Firma kodu, kullanıcı adı veya şifre hatalı."
        //    → Saldırgan firma kodunun yanlış mı, kullanıcı adının mı,
        //      şifrenin mi hatalı olduğunu ayırt edemez.
        // ════════════════════════════════════════════════════════════════
        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == model.FirmaKodu);

        var tennene = tenant;

        // Tenant yok, pasif veya süresi dolmuş — tek genel hata
        bool tenantInvalid =
            tenant == null ||
            !tenant.IsActive ||
            (tenant.TrialEndsAt.HasValue && tenant.TrialEndsAt.Value < DateTime.UtcNow);

        if (tenantInvalid)
        {
            // Timing attack önlemi: geçersiz firma kodu ile geçerli firma kodu
            // arasındaki yanıt süresi farkını yapay gecikme ile kapat.
            await Task.Delay(Random.Shared.Next(100, 300));
            ModelState.AddModelError(string.Empty, "Firma kodu, kullanıcı adı veya şifre hatalı.");
            return View(model);
        }

        // ════════════════════════════════════════════════════════════════
        //  AŞAMA 2 — Kullanıcı Adı Birleştirme
        //
        //  Admin "ahmet" girdi → fullUsername = "burger-palace-a1b2_ahmet"
        //  Bu değer Identity'nin AspNetUsers.UserName sütunuyla eşleşir.
        // ════════════════════════════════════════════════════════════════
        var fullUsername = $"{model.FirmaKodu}_{model.Username}";

        // ════════════════════════════════════════════════════════════════
        //  AŞAMA 3 — Standart Identity Doğrulama
        // ════════════════════════════════════════════════════════════════
        var user = await _userManager.FindByNameAsync(fullUsername);
        if (user == null)
        {
            // Kullanıcı bu tenant'ta yok — yine genel mesaj (bilgi sızdırma önlemi)
            await Task.Delay(Random.Shared.Next(100, 300));
            ModelState.AddModelError(string.Empty, "Firma kodu, kullanıcı adı veya şifre hatalı.");
            return View(model);
        }

        // Lockout kontrolü
        if (await _userManager.IsLockedOutAsync(user))
        {
            ModelState.AddModelError(string.Empty, "Hesap kilitlendi. 15 dakika sonra tekrar deneyin.");
            return View(model);
        }

        // Şifre kontrolü
        var passwordOk = await _userManager.CheckPasswordAsync(user, model.Password);
        if (!passwordOk)
        {
            await _userManager.AccessFailedAsync(user);
            // Şifre hatalı — yine genel mesaj
            ModelState.AddModelError(string.Empty, "Firma kodu, kullanıcı adı veya şifre hatalı.");
            return View(model);
        }

        var roles = await _userManager.GetRolesAsync(user);

        // SysAdmin restoran paneline giremez
        if (roles.Contains("SysAdmin"))
        {
            ModelState.AddModelError(string.Empty, "Bu panele erişim yetkiniz yok.");
            return View(model);
        }

        // ── Başarılı Giriş ────────────────────────────────────────────────
        await _userManager.ResetAccessFailedCountAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // AppAuth cookie — TenantId claim → HttpContextTenantService → Global Query Filter
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name,           user.UserName!),
            new Claim("FullName",                user.FullName),
            new Claim("TenantId",                user.TenantId ?? ""),
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var identity = new ClaimsIdentity(claims, "AppAuth");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("AppAuth", principal, new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
        });

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        // Kitchen rolü doğrudan mutfak ekranına gider
        if (roles.Contains("Kitchen"))
            return RedirectToAction("Display", "Kitchen", new { area = "App" });

        return RedirectToAction("Index", "Tables", new { area = "App" });
    }

    // ── POST /App/Auth/Logout ──────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("AppAuth");
        return RedirectToAction(nameof(Login), new { area = "App" });
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();
}
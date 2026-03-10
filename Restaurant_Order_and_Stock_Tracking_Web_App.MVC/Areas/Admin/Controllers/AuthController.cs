// ============================================================================
//  Areas/Admin/Controllers/AuthController.cs
//  GÖREV 1 — SysAdmin Auth Controller
//
//  Mimari Notlar:
//  • [Area("Admin")] + [AllowAnonymous] — route sistemi ve güvenlik
//  • Login (GET): AdminAuth scheme'i ile zaten giriş yapıldıysa Dashboard'a yönlendir
//  • Login (POST): FindByNameAsync → SysAdmin rol kontrolü → AdminAuth cookie
//  • Logout: YALNIZCA AdminAuth scheme'ini temizler (AppAuth'a dokunmaz)
//  • TenantId claim kasıtlı eklenmez → Global Query Filter bypass → tüm DB görünür
// ============================================================================

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Auth;
using System.Security.Claims;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.Admin.Controllers;

[Area("Admin")]
[AllowAnonymous]
public class AuthController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    // ── GET /Admin/Auth/Login ────────────────────────────────────────────────
    // Kullanıcı zaten AdminAuth ile giriş yapmışsa direkt Dashboard'a yönlendir.
    // User.Identity.IsAuthenticated yalnızca aktif scheme'e bakar;
    // ancak burada AdminAuth'u açıkça kontrol ediyoruz.
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        // AdminAuth scheme'i ile mevcut bir oturum var mı?
        var authResult = await HttpContext.AuthenticateAsync("AdminAuth");
        if (authResult.Succeeded && authResult.Principal?.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home", new { area = "Admin" });

        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    // ── POST /Admin/Auth/Login ───────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
            return View(model);

        // 1. Kullanıcıyı bul
        var user = await _userManager.FindByNameAsync(model.Username);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Kullanıcı adı veya şifre hatalı.");
            return View(model);
        }

        // 2. Hesap kilitli mi?
        if (await _userManager.IsLockedOutAsync(user))
        {
            ModelState.AddModelError(string.Empty, "Hesap kilitlendi. Lütfen daha sonra tekrar deneyin.");
            return View(model);
        }

        // 3. Şifre kontrolü
        var passwordOk = await _userManager.CheckPasswordAsync(user, model.Password);
        if (!passwordOk)
        {
            await _userManager.AccessFailedAsync(user);
            ModelState.AddModelError(string.Empty, "Kullanıcı adı veya şifre hatalı.");
            return View(model);
        }

        // 4. ── GÜVENLİK DUVARI ──────────────────────────────────────────────
        //    Şifre doğru olsa bile SysAdmin rolü olmayan kullanıcılar reddedilir.
        //    Güvenlik sebebiyle genel bir hata mesajı gösterilir.
        var roles = await _userManager.GetRolesAsync(user);
        if (!roles.Contains("SysAdmin"))
        {
            // Başarısız giriş sayacını artırarak brute-force korumasını aktif tut
            await _userManager.AccessFailedAsync(user);
            ModelState.AddModelError(string.Empty, "Kullanıcı adı veya şifre hatalı.");
            return View(model);
        }

        // 5. Başarılı giriş — sayacı sıfırla ve güvenlik stamp'ini güncelle
        await _userManager.ResetAccessFailedCountAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // 6. AdminAuth scheme ile manuel cookie oluştur
        //    ÖNEMLİ: TenantId claim'i kasıtlı olarak EKLENMEZ.
        //    → ITenantService.TenantId null kalır
        //    → Global Query Filter "TenantId == null" dalına girer
        //    → SysAdmin tüm tenant verilerini görebilir
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name,           user.UserName!),
            new Claim("FullName",                user.FullName),
            new Claim(ClaimTypes.Role,           "SysAdmin"),
        };

        var identity = new ClaimsIdentity(claims, "AdminAuth");
        var principal = new ClaimsPrincipal(identity);

        var properties = new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = model.RememberMe
                ? DateTimeOffset.UtcNow.AddDays(30)
                : DateTimeOffset.UtcNow.AddHours(8)
        };

        await HttpContext.SignInAsync("AdminAuth", principal, properties);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Home", new { area = "Admin" });
    }

    // ── POST /Admin/Auth/Logout ──────────────────────────────────────────────
    // SADECE AdminAuth scheme'ini temizler.
    // AppAuth (App Area oturumu) bu işlemden etkilenmez.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("AdminAuth");
        return RedirectToAction(nameof(Login), new { area = "Admin" });
    }

    // ── GET /Admin/Auth/AccessDenied ─────────────────────────────────────────
    public IActionResult AccessDenied() => View();
}
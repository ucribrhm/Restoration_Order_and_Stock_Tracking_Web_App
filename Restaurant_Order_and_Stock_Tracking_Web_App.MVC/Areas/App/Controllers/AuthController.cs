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

    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Tables", new { area = "App" });

        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.FindByNameAsync(model.Username);
        if (user == null)
        {
            ModelState.AddModelError(string.Empty, "Kullanıcı adı veya şifre hatalı.");
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
            ModelState.AddModelError(string.Empty, "Kullanıcı adı veya şifre hatalı.");
            return View(model);
        }

        var roles = await _userManager.GetRolesAsync(user);

        // SysAdmin restoran paneline giremez
        if (roles.Contains("SysAdmin"))
        {
            ModelState.AddModelError(string.Empty, "Bu panele erişim yetkiniz yok.");
            return View(model);
        }

        // Tenant aktiflik kontrolü
        if (!string.IsNullOrEmpty(user.TenantId))
        {
            var tenant = await _db.Tenants
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TenantId == user.TenantId);

            if (tenant == null || !tenant.IsActive)
            {
                ModelState.AddModelError(string.Empty, "Restoranınızın aboneliği aktif değil. Lütfen destek ile iletişime geçin.");
                return View(model);
            }
        }

        // Başarılı giriş — tekil oturum için stamp güncelle
        await _userManager.ResetAccessFailedCountAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // AppAuth scheme ile manuel cookie oluştur
        // TenantId claim eklenir → HttpContextTenantService okur → Global Query Filter aktif
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
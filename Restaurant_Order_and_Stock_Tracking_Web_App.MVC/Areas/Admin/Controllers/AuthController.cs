using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Auth;
using System.Security.Claims;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.Admin.Controllers;

[Area("Admin")]
public class AuthController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Home", new { area = "Admin" });

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

        // Sadece SysAdmin bu panele girebilir
        var roles = await _userManager.GetRolesAsync(user);
        if (!roles.Contains("SysAdmin"))
        {
            ModelState.AddModelError(string.Empty, "Bu panele erişim yetkiniz yok.");
            return View(model);
        }

        // Başarılı giriş — tekil oturum için stamp güncelle
        await _userManager.ResetAccessFailedCountAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // AdminAuth scheme ile manuel cookie oluştur
        // TenantId claim kasıtlı eklenmez → Global Query Filter bypass → tüm tenant'lar görünür
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Name,           user.UserName!),
            new Claim("FullName",                user.FullName),
            new Claim(ClaimTypes.Role,           "SysAdmin"),
        };

        var identity = new ClaimsIdentity(claims, "AdminAuth");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync("AdminAuth", principal, new AuthenticationProperties
        {
            IsPersistent = model.RememberMe,
            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
        });

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Home", new { area = "Admin" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync("AdminAuth");
        return RedirectToAction(nameof(Login), new { area = "Admin" });
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();
}
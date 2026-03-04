using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Auth;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Controllers;

public class AuthController : Controller
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            if (User.IsInRole("Admin"))
                return RedirectToAction("Index", "Home");
            return RedirectToAction("Index", "Tables");
        }
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

        // ── TEKİL OTURUM: Stamp'i PasswordSignInAsync'TEN ÖNCE güncelle ─────
        //
        // NEDEN ÖNCE?
        // PasswordSignInAsync, yeni auth cookie'yi o anki SecurityStamp ile
        // imzalar. Eğer stamp LOGIN'DEN SONRA değiştirilirse, cookie içindeki
        // stamp ile DB'deki stamp 30 saniye içinde uyuşmaz → oturum düşer.
        //
        // ÖNCE güncellenirse: yeni cookie yeni stamp ile oluşturulur → tutarlı.
        // Eski açık oturumların cookie'lerindeki eski stamp 30 saniyede geçersiz
        // kalır → tek oturum zorlaması çalışır, mevcut oturum düşmez.
        // ─────────────────────────────────────────────────────────────────────
        await _userManager.UpdateSecurityStampAsync(user);

        var result = await _signInManager.PasswordSignInAsync(
            user, model.Password, model.RememberMe, lockoutOnFailure: false);

        if (result.Succeeded)
        {
            user.LastLoginAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);

            // PasswordSignInAsync sonrası User.IsInRole() bu request'te çalışmaz
            // (cookie pipeline'a henüz işlenmedi) → DB'den oku
            var roles = await _userManager.GetRolesAsync(user);

            if (roles.Count == 0)
            {
                await _signInManager.SignOutAsync();
                ModelState.AddModelError(string.Empty,
                    "Hesabınıza henüz bir rol atanmamış. Lütfen Admin ile iletişime geçin.");
                return View(model);
            }

            if (roles.Contains("Admin"))
            {
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                    return Redirect(returnUrl);
                return RedirectToAction("Index", "Home");
            }
            else if (roles.Contains("Kasiyer"))
            {
                
                return RedirectToAction("Index","Tables");
            }
            else if (roles.Contains("Garson"))
            {

                return RedirectToAction("Index", "Tables");
            }
            else if (roles.Contains("Kitchen"))
            {

                return RedirectToAction("Display", "Kitchen");
            }
            // Güvenlik: Hiçbirine uymuyorsa yetkisiz erişim sayfasına veya ana sayfaya at
            return RedirectToAction("AccessDenied", "Auth");
            //return RedirectToAction("Index", "Tables");
        }

        ModelState.AddModelError(string.Empty, "Kullanıcı adı veya şifre hatalı.");
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();
}
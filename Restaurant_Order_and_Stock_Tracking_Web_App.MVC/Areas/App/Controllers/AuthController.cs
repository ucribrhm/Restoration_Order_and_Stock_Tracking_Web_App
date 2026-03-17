// ════════════════════════════════════════════════════════════════════════════
//  Areas/App/Controllers/AuthController.cs
//
//  SPRINT C — [SC-2] Workspace Login Akışı
//  SPRINT 4 — [IMP-4] Impersonation Giriş ve Çıkış
//  SPRINT 5 — [OTP-5] Şifre Sıfırlama (ForgotPassword, VerifyResetOtp, ResetPassword)
// ════════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Auth;
using System.Security.Claims;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.App.Controllers;

[Area("App")]
public class AuthController : AppBaseController
{
    // ── Bağımlılıklar ─────────────────────────────────────────────────────
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RestaurantDbContext _db;
    private readonly ILogger<AuthController> _logger;
    private readonly IOtpService _otpService;
    private readonly IEmailSender _emailSender;
    private readonly IMemoryCache _cache;
    private readonly ITenantFeatureService _featureService;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        RestaurantDbContext db,
        ILogger<AuthController> logger,
        IOtpService otpService,
        IEmailSender emailSender,
        IMemoryCache cache,
        ITenantFeatureService featureService)
    {
        _userManager = userManager;
        _db = db;
        _logger = logger;
        _otpService = otpService;
        _emailSender = emailSender;
        _cache = cache;
        _featureService = featureService;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  LOGIN / LOGOUT
    // ══════════════════════════════════════════════════════════════════════

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
    [EnableRateLimiting("LoginPolicy")]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
            return View(model);

        // ── AŞAMA 1: Tenant doğrulama + timing attack koruması ────────────
        var tenant = await _db.Tenants
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == model.FirmaKodu);

        bool tenantInvalid =
            tenant == null ||
            !tenant.IsActive ||
            (tenant.TrialEndsAt.HasValue && tenant.TrialEndsAt.Value < DateTime.UtcNow);

        if (tenantInvalid)
        {
            await Task.Delay(Random.Shared.Next(100, 300));
            ModelState.AddModelError(string.Empty, "Firma kodu, kullanıcı adı veya şifre hatalı.");
            return View(model);
        }

        // ── AŞAMA 2: Kullanıcı adı birleştirme ────────────────────────────
        var fullUsername = $"{model.FirmaKodu.Trim()}_{model.Username.Trim()}";
        var normalizedFullUsername = _userManager.NormalizeName(fullUsername);

        // ── AŞAMA 3: Identity doğrulama ────────────────────────────────────
        var user = await _userManager.Users.FirstOrDefaultAsync(u =>
            u.TenantId == model.FirmaKodu &&
            u.NormalizedUserName == normalizedFullUsername);

        if (user == null)
        {
            await Task.Delay(Random.Shared.Next(100, 300));
            ModelState.AddModelError(string.Empty, "Firma kodu, kullanıcı adı veya şifre hatalı.");
            return View(model);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            ModelState.AddModelError(string.Empty, "Hesap kilitlendi. 15 dakika sonra tekrar deneyin.");
            return View(model);
        }

        var passwordOk = await _userManager.CheckPasswordAsync(user, model.Password);
        if (!passwordOk)
        {
            await _userManager.AccessFailedAsync(user);
            ModelState.AddModelError(string.Empty, "Firma kodu, kullanıcı adı veya şifre hatalı.");
            return View(model);
        }

        var roles = await _userManager.GetRolesAsync(user);

        if (roles.Contains("SysAdmin"))
        {
            ModelState.AddModelError(string.Empty, "Bu panele erişim yetkiniz yok.");
            return View(model);
        }

        // ── Başarılı giriş ─────────────────────────────────────────────────
        await _userManager.ResetAccessFailedCountAsync(user);
        await _userManager.UpdateSecurityStampAsync(user);
        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name,           user.UserName!),
            new("FullName",                user.FullName),
            new("TenantId",                user.TenantId ?? ""),
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

        // ── Proaktif Cache Yükleme ─────────────────────────────────────────
        // Login anında tenant feature'larını önceden cache'e yükle.
        // İlk sayfa açılışında cache miss yaşanmaz; DB sorgusu burada tamamlanır.
        // await edilir: Scoped DbContext fire-and-forget'te dispose edilebilir.
        // Hata olursa try-catch ile login akışı korunur.
        try
        {
            await _featureService.GetFeaturesAsync(user.TenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AuthController] Feature cache warm-up başarısız. TenantId: {TenantId}",
                user.TenantId);
            // Cache yükleme hatası login'i engellememeli — devam et
        }

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

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

    // ══════════════════════════════════════════════════════════════════════
    //  IMPERSONATION  [IMP-4]
    // ══════════════════════════════════════════════════════════════════════

    // ── GET /App/Auth/Impersonate?token={uuid} ────────────────────────────
    [AllowAnonymous]
    [HttpGet]
    public async Task<IActionResult> Impersonate(string token)
    {
        if (!Guid.TryParse(token, out var tokenGuid))
        {
            _logger.LogWarning("[IMPERSONATION] Geçersiz token formatı. IP: {Ip}", GetClientIp());
            return Forbid();
        }

        var clientIp = GetClientIp();
        var now = DateTime.UtcNow;

        var record = await _db.ImpersonationTokens
            .FirstOrDefaultAsync(t =>
                t.TokenId == tokenGuid &&
                t.UsedAt == null &&
                t.ExpiresAt > now);

        if (record == null)
        {
            _logger.LogWarning(
                "[IMPERSONATION] Token geçersiz veya kullanılmış. TokenId: {TokenId} | IP: {Ip}",
                tokenGuid, clientIp);
            return Forbid();
        }

        record.UsedAt = now;
        record.UsedFromIp = clientIp;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _logger.LogWarning("[IMPERSONATION] Race condition. TokenId: {TokenId}", tokenGuid);
            return Forbid();
        }

        var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
        var targetAdmin = adminUsers.FirstOrDefault(u => u.TenantId == record.TargetTenantId);

        if (targetAdmin == null)
        {
            _logger.LogError("[IMPERSONATION] Admin kullanıcı bulunamadı. TenantId: {TenantId}",
                record.TargetTenantId);
            return NotFound("Bu restorana ait yönetici hesabı bulunamadı.");
        }

        var impClaims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, targetAdmin.Id),
            new(ClaimTypes.Name,           targetAdmin.UserName ?? ""),
            new("FullName",                targetAdmin.FullName  ?? ""),
            new("TenantId",                record.TargetTenantId),
            new(ClaimTypes.Role,           record.TargetRole),
            new("IsImpersonation",         "true"),
            new("ImpersonatedBy",          record.SysAdminId),
        };

        var impIdentity = new ClaimsIdentity(impClaims, "AppAuth");
        var impPrincipal = new ClaimsPrincipal(impIdentity);

        await HttpContext.SignInAsync("AppAuth", impPrincipal, new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(30)
        });

        _logger.LogWarning(
            "[IMPERSONATION] Giriş başarılı. SysAdmin: {SysAdminId} → Tenant: {TenantId} | IP: {Ip}",
            record.SysAdminId, record.TargetTenantId, clientIp);

        return RedirectToAction("Index", "Tables", new { area = "App" });
    }

    // ── POST /App/Auth/EndImpersonation ───────────────────────────────────
    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EndImpersonation()
    {
        var isImpersonation = User.FindFirstValue("IsImpersonation") == "true";
        var sysAdminId = User.FindFirstValue("ImpersonatedBy") ?? "?";
        var tenantId = User.FindFirstValue("TenantId") ?? "?";

        if (!isImpersonation)
            return RedirectToAction("Index", "Tables", new { area = "App" });

        await HttpContext.SignOutAsync("AppAuth");

        _logger.LogWarning(
            "[IMPERSONATION] Oturum sonlandırıldı. SysAdmin: {SysAdminId} ← Tenant: {TenantId}",
            sysAdminId, tenantId);

        return Redirect("/Admin/Home/Index");
    }

    // ══════════════════════════════════════════════════════════════════════
    //  ŞİFRE SIFIRLAMA  [OTP-5]
    // ══════════════════════════════════════════════════════════════════════

    // ── GET /App/Auth/ForgotPassword ──────────────────────────────────────
    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPassword()
    {
        ViewData["Title"] = "Şifre Sıfırlama";
        return View();
    }

    // ── POST /App/Auth/ForgotPassword ─────────────────────────────────────
    // [FP-1] Yalnızca firmaKodu alır — admin e-postasını sistem bulur.
    // [FP-2] Enumeration koruması: tenant var/yok — aynı mesaj + Task.Delay.
    // [FP-3] Tenant cooldown: IOtpService.IsCooldownActive() ile spam engeli.
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("ForgotPasswordPolicy")]
    public async Task<IActionResult> ForgotPassword(string firmaKodu)
    {
        ViewData["Title"] = "Şifre Sıfırlama";

        // Her durumda aynı mesaj — tenant var/yok bilgisi sızdırılmaz
        const string safeMessage = "Firma kodunuz sistemde kayıtlıysa yönetici e-postasına kod gönderildi.";

        if (string.IsNullOrWhiteSpace(firmaKodu))
        {
            ViewBag.Info = safeMessage;
            return View();
        }

        // [FP-2] Timing attack koruması — her iki dalda da ~200ms bekleme
        var delay = Task.Delay(Random.Shared.Next(150, 250));

        // Tenant'ın Admin kullanıcısını bul (email kullanıcıdan alınmıyor)
        var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
        var adminUser = adminUsers.FirstOrDefault(u => u.TenantId == firmaKodu);

        if (adminUser?.Email != null)
        {
            var email = adminUser.Email;

            // [FP-3] Tenant cooldown — aynı firmaya 60s içinde tekrar OTP atma
            if (!_otpService.IsCooldownActive(email, OtpPurpose.ResetPassword))
            {
                var code = _otpService.Generate(email, OtpPurpose.ResetPassword);
                _emailSender.EnqueueEmail(
                    to: email,
                    subject: "RestaurantOS — Şifre Sıfırlama Kodu",
                    htmlBody: EmailTemplates.OtpEmail("resetpassword", code));

                _logger.LogInformation("[RESET] OTP gönderildi. TenantId: {TenantId}", firmaKodu);
            }
            else
            {
                _logger.LogWarning("[RESET] Cooldown aktif, OTP atlanıyor. TenantId: {TenantId}", firmaKodu);
            }

            // Maskeleme: sadece domain kısmını göster (örn: "***@restoran.com")
            var atIdx = email.IndexOf('@');
            var maskedEmail = atIdx > 0 ? "***" + email[atIdx..] : "***";
            ViewBag.MaskedEmail = maskedEmail;
        }

        await delay; // Timing saldırısını önlemek için sabit bekleme tamamlanır

        ViewBag.Info = safeMessage;
        ViewBag.FirmaKodu = firmaKodu;
        return View("VerifyResetOtp");
    }

    // ── POST /App/Auth/VerifyResetOtp ─────────────────────────────────────
    // [FP-4] Email kullanıcıdan alınmaz — firmaKodu üzerinden Admin emaili bulunur.
    // [FP-5] Başarıda: Guid reset_grant token → IMemoryCache (5dk) + TempData.
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("OtpVerifyPolicy")]
    public async Task<IActionResult> VerifyResetOtp(string firmaKodu, string code)
    {
        ViewData["Title"] = "Kod Doğrulama";

        // Admin emailini yeniden bul (form'da email taşınmıyor)
        var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
        var adminUser = adminUsers.FirstOrDefault(u => u.TenantId == firmaKodu);
        var email = adminUser?.Email ?? "";

        if (string.IsNullOrEmpty(email))
        {
            ViewBag.FirmaKodu = firmaKodu;
            ViewBag.Error = "Doğrulama başarısız.";
            return View("VerifyResetOtp");
        }

        var result = _otpService.Verify(email, OtpPurpose.ResetPassword, code?.Trim() ?? "");

        if (result == OtpVerifyResult.Success)
        {
            _otpService.Consume(email, OtpPurpose.ResetPassword);

            // [FP-5] Tek kullanımlık reset grant — IMemoryCache, 5 dakika TTL
            var resetGuid = Guid.NewGuid().ToString("N");
            _cache.Set(
                $"reset_grant:{resetGuid}",
                firmaKodu,
                new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
                {
                    AbsoluteExpiration = DateTimeOffset.UtcNow.AddMinutes(5)
                });

            TempData["ResetToken"] = resetGuid;
            TempData["ResetFirmaKodu"] = firmaKodu;

            _logger.LogInformation("[RESET] Grant token üretildi. TenantId: {TenantId}", firmaKodu);
            return RedirectToAction(nameof(ResetPassword), new { area = "App" });
        }

        ViewBag.FirmaKodu = firmaKodu;
        ViewBag.CooldownSec = _otpService.GetCooldownSeconds(email, OtpPurpose.ResetPassword);
        ViewBag.Error = result switch
        {
            OtpVerifyResult.InvalidCode => "Hatalı kod. Lütfen tekrar deneyin.",
            OtpVerifyResult.Locked => "Çok fazla hatalı deneme. 15 dakika bekleyin.",
            OtpVerifyResult.Expired => "Kodun süresi dolmuş. Yeni kod isteyin.",
            _ => "Doğrulama başarısız."
        };
        return View("VerifyResetOtp");
    }

    // ── GET /App/Auth/ResetPassword ───────────────────────────────────────
    // [FP-6] TempData token varlığını kontrol et. Cache'e bakmaz (GET'te token korunur).
    [AllowAnonymous]
    [HttpGet]
    public IActionResult ResetPassword()
    {
        if (TempData["ResetToken"] is not string token || string.IsNullOrEmpty(token))
            return RedirectToAction(nameof(ForgotPassword), new { area = "App" });

        TempData.Keep(); // POST'a kadar TempData korunsun
        ViewBag.ResetToken = token;
        ViewBag.ResetFirmaKodu = TempData["ResetFirmaKodu"] as string ?? "";
        TempData.Keep();
        ViewData["Title"] = "Yeni Şifre Belirleyin";
        return View();
    }

    // ── POST /App/Auth/ResetPassword ─────────────────────────────────────
    // [FP-7] resetToken cache'te doğrulanır (TempData yetmez — ikili kontrol).
    // [FP-8] Başarıda cache token silinir — tek kullanımlık garantisi.
    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(
        string resetToken, string firmaKodu, string newPassword, string confirmPassword)
    {
        ViewData["Title"] = "Yeni Şifre Belirleyin";

        // [FP-7] Cache doğrulaması — token var mı ve bu tenant'a mı ait?
        var cacheKey = $"reset_grant:{resetToken}";
        var cachedTenantId = _cache.Get<string>(cacheKey);

        if (string.IsNullOrEmpty(cachedTenantId) || cachedTenantId != firmaKodu)
        {
            _logger.LogWarning("[RESET] Geçersiz veya süresi dolmuş token. TenantId: {TenantId}", firmaKodu);
            TempData["ResetError"] = "Şifre sıfırlama oturumunuzun süresi dolmuş. Lütfen tekrar başlayın.";
            return RedirectToAction(nameof(ForgotPassword), new { area = "App" });
        }

        if (newPassword != confirmPassword)
        {
            ViewBag.Error = "Şifreler eşleşmiyor.";
            ViewBag.ResetToken = resetToken;
            ViewBag.ResetFirmaKodu = firmaKodu;
            return View();
        }

        // Admin kullanıcısını firmaKodu ile bul
        var adminUsers = await _userManager.GetUsersInRoleAsync("Admin");
        var user = adminUsers.FirstOrDefault(u => u.TenantId == firmaKodu);

        if (user == null)
        {
            ViewBag.Error = "Kullanıcı bulunamadı.";
            ViewBag.ResetToken = resetToken;
            ViewBag.ResetFirmaKodu = firmaKodu;
            return View();
        }

        var identityToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, identityToken, newPassword);

        if (!result.Succeeded)
        {
            ViewBag.Error = string.Join(" ", result.Errors.Select(e => e.Description));
            ViewBag.ResetToken = resetToken;
            ViewBag.ResetFirmaKodu = firmaKodu;
            return View();
        }

        // [FP-8] Token tek kullanımlık — başarıda sil
        _cache.Remove(cacheKey);

        await _userManager.UpdateSecurityStampAsync(user);
        _logger.LogInformation("[RESET] Şifre başarıyla sıfırlandı. TenantId: {TenantId}", firmaKodu);
        TempData["ResetSuccess"] = "Şifreniz başarıyla güncellendi. Giriş yapabilirsiniz.";
        return RedirectToAction(nameof(Login), new { area = "App" });
    }

    // ══════════════════════════════════════════════════════════════════════
    //  YARDIMCILAR
    // ══════════════════════════════════════════════════════════════════════

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    private string GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString()
        ?? HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault()
        ?? "bilinmiyor";
}
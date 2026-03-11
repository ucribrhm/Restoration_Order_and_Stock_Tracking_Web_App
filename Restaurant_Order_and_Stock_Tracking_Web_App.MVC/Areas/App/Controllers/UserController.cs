// ════════════════════════════════════════════════════════════════════════════
//  Areas/App/Controllers/UserController.cs
//  Yol: Areas/App/Controllers/UserController.cs
//
//  SPRINT 2 — Identity Güvenliği & N+1 Optimizasyonu (korundu):
//  [G-02] Çapraz-tenant kullanıcı erişimi kapatıldı.
//  [G-03] TenantId zorunlu — null bypass önlendi.
//  [P-01] N+1 → tek LEFT JOIN sorgusu.
//  [G-02-DEL] Tenant'a özgü admin sayımı.
//
//  SPRINT B — [SB-2/SB-3] Workspace Login Veri Maskeleme:
//  [SB-2] Index: UserName → ToDisplayName(tenantId) ile maskelendi.
//         DB'deki "burger-palace-a1b2_ahmet" → UI'da sadece "ahmet" görünür.
//  [SB-3] Create POST: Admin "ahmet" girer; controller $"{tenantId}_{kısaAd}"
//         olarak birleştirir ve o şekilde DB'ye yazar.
//         FindByNameAsync çakışma kontrolü tam (prefix'li) username ile yapılır.
//  [SB-4] Edit GET: Edit formuna da sadece kısa ad gösterilir.
//  [SB-5] Edit POST: Gelen kısa adı tenantId ile birleştirip DB'ye yazar.
//         Çakışma kontrolü prefix'li username ile yapılır.
// ════════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Extensions;   // [SB-2] ToDisplayName
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared.Common;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Users;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.App.Controllers;

[Area("App")]
[Authorize(Roles = "Admin")]
public class UserController : AppBaseController
{
    private static readonly string[] AllowedRoles = { "Admin", "Garson", "Kasiyer", "Kitchen" };

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly RestaurantDbContext _db;
    private readonly ITenantService _tenantService;

    public UserController(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        RestaurantDbContext db,
        ITenantService tenantService)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
        _tenantService = tenantService;
    }

    // ── GET /App/User ──────────────────────────────────────────────────────
    // [SB-2] UserName kolonunda prefix maskeleme uygulandı.
    //        DB'deki "burger-palace-a1b2_ahmet" → UI'da "ahmet" görünür.
    //        ToDisplayName() extension metodu Extensions/StringExtensions.cs'de.
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Kullanıcı Yönetimi";

        var tenantId = _tenantService.TenantId;

        // [G-02 + P-01] Tek LEFT JOIN — kullanıcı + rolü tek round-trip'te çek.
        var model = await (
            from u in _db.Users
                        .Where(u => u.TenantId == tenantId)
            join ur in _db.UserRoles on u.Id equals ur.UserId into urs
            from ur in urs.DefaultIfEmpty()
            join r in _db.Roles on ur.RoleId equals r.Id into rs
            from r in rs.DefaultIfEmpty()
            select new UserListItemViewModel
            {
                Id = u.Id,
                // [SB-2] Ham username'i UI'a vermeden önce prefix'i soy.
                //        "burger-palace-a1b2_ahmet" → "ahmet"
                UserName = (u.UserName ?? string.Empty).ToDisplayName(tenantId),
                FullName = u.FullName,
                Email = u.Email,
                PhoneNumber = u.PhoneNumber,
                Role = r != null ? r.Name ?? "— Rol Yok —" : "— Rol Yok —",
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt
            }
        ).ToListAsync();

        return View(model);
    }

    // ── GET /App/User/Create ───────────────────────────────────────────────
    public IActionResult Create()
    {
        ViewData["Title"] = "Yeni Kullanıcı";
        ViewBag.Roles = AllowedRoles;
        return View();
    }

    // ── POST /App/User/Create ──────────────────────────────────────────────
    // [SB-3] Admin forma sadece kısa adı ("ahmet") girer.
    //        Controller arka planda tam username'i inşa eder:
    //          fullUsername = $"{tenantId}_{model.UserName.Trim()}"
    //        Çakışma kontrolü ve CreateAsync tam username ile çalışır.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserCreateViewModel model)
    {
        var tenantId = _tenantService.TenantId;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            TempData["Error"] = "Kritik Hata: Dükkan kimliği (TenantId) bulunamadı. Dükkansız personel oluşturulamaz!";
            return RedirectToAction(nameof(Index));
        }

        if (!string.IsNullOrEmpty(model.Role) && !AllowedRoles.Contains(model.Role))
            ModelState.AddModelError("Role", "Geçersiz rol seçimi.");

        if (!ModelState.IsValid)
        {
            ViewBag.Roles = AllowedRoles;
            return View(model);
        }

        // [SB-3] Tam username: "{tenantId}_{kısaAd}"
        // Admin "ahmet" girer → DB'ye "burger-palace-a1b2_ahmet" yazılır.
        var fullUsername = $"{tenantId}_{model.UserName.Trim()}";

        // Çakışma kontrolü tam username ile — kısa ad ile DEĞİL.
        // (başka restoranda "ahmet" olabilir; bu restoranın "ahmet"i çakışır.)
        if (await _userManager.FindByNameAsync(fullUsername) != null)
        {
            ModelState.AddModelError("UserName", "Bu kullanıcı adı zaten kullanılıyor.");
            ViewBag.Roles = AllowedRoles;
            return View(model);
        }

        if (!await _roleManager.RoleExistsAsync(model.Role))
            await _roleManager.CreateAsync(new IdentityRole(model.Role));

        var user = new ApplicationUser
        {
            UserName = fullUsername,            // [SB-3] Prefix'li tam username DB'ye gider
            FullName = model.FullName.Trim(),
            Email = model.Email?.Trim(),
            PhoneNumber = model.PhoneNumber,
            CreatedAt = DateTime.UtcNow,
            EmailConfirmed = true,
            TenantId = tenantId                 // [G-03] TenantId zorunlu
        };

        var createResult = await _userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            foreach (var e in createResult.Errors)
                ModelState.AddModelError(string.Empty, e.Description);
            ViewBag.Roles = AllowedRoles;
            return View(model);
        }

        var roleResult = await _userManager.AddToRoleAsync(user, model.Role);
        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);
            foreach (var e in roleResult.Errors)
                ModelState.AddModelError(string.Empty, $"Rol ataması başarısız: {e.Description}");
            ViewBag.Roles = AllowedRoles;
            return View(model);
        }

        TempData["Success"] = $"'{user.FullName}' kullanıcısı '{model.Role}' rolüyle oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }

    // ── GET /App/User/Edit/{id} ────────────────────────────────────────────
    // [SB-4] Edit formuna prefix soyulmuş kısa ad gösterilir.
    //        Kullanıcı "ahmet" görür, güncelleme yaparken de "ahmet" girer.
    public async Task<IActionResult> Edit(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        // [G-02] Çapraz-tenant erişim engeli
        if (user.TenantId != _tenantService.TenantId) return NotFound();

        var roles = await _userManager.GetRolesAsync(user);
        ViewData["Title"] = "Kullanıcı Düzenle";
        ViewBag.Roles = AllowedRoles;

        if (!roles.Any())
            TempData["Warning"] = "Bu kullanıcıya henüz rol atanmamış! Aşağıdan bir rol seçip kaydedin.";

        return View(new UserEditViewModel
        {
            Id = user.Id,
            // [SB-4] Ham username'i formda göstermeden önce prefix'i soy.
            UserName = (user.UserName ?? "").ToDisplayName(_tenantService.TenantId),
            FullName = user.FullName.Trim(),
            Email = user.Email?.Trim(),
            PhoneNumber = user.PhoneNumber,
            Role = roles.FirstOrDefault() ?? ""
        });
    }

    // ── POST /App/User/Edit ────────────────────────────────────────────────
    // [SB-5] Admin kısa adı ("ahmet") günceller.
    //        Controller arka planda tam username'i yeniden inşa eder.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(UserEditViewModel model)
    {
        if (!string.IsNullOrEmpty(model.Role) && !AllowedRoles.Contains(model.Role))
            ModelState.AddModelError("Role", "Geçersiz rol seçimi.");

        if (!ModelState.IsValid)
        {
            ViewBag.Roles = AllowedRoles;
            return View(model);
        }

        var user = await _userManager.FindByIdAsync(model.Id);
        if (user == null) return NotFound();

        // [G-02] Çapraz-tenant POST engeli
        if (user.TenantId != _tenantService.TenantId) return NotFound();

        var tenantId = _tenantService.TenantId;

        // [SB-5] Yeni tam username: "{tenantId}_{kısaAd}"
        var newFullUsername = $"{tenantId}_{model.UserName.Trim()}";

        // Kullanıcı adı değiştiyse çakışma kontrolü yap
        if (user.UserName != newFullUsername)
        {
            if (await _userManager.FindByNameAsync(newFullUsername) != null)
            {
                ModelState.AddModelError("UserName", "Bu kullanıcı adı zaten kullanılıyor.");
                ViewBag.Roles = AllowedRoles;
                return View(model);
            }
        }

        user.UserName = newFullUsername;        // [SB-5] Prefix'li tam username kaydedilir
        user.FullName = model.FullName;
        user.Email = model.Email;
        user.PhoneNumber = model.PhoneNumber;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            foreach (var e in updateResult.Errors)
                ModelState.AddModelError(string.Empty, e.Description);
            ViewBag.Roles = AllowedRoles;
            return View(model);
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        await _userManager.RemoveFromRolesAsync(user, currentRoles);

        if (!await _roleManager.RoleExistsAsync(model.Role))
            await _roleManager.CreateAsync(new IdentityRole(model.Role));

        var roleResult = await _userManager.AddToRoleAsync(user, model.Role);
        if (!roleResult.Succeeded)
        {
            foreach (var e in roleResult.Errors)
                ModelState.AddModelError(string.Empty, $"Rol ataması başarısız: {e.Description}");
            ViewBag.Roles = AllowedRoles;
            return View(model);
        }

        // Rol değiştiyse mevcut oturumu güvenli şekilde geçersiz kıl
        await _userManager.UpdateSecurityStampAsync(user);

        TempData["Success"] = $"'{user.FullName}' güncellendi.";
        return RedirectToAction(nameof(Index));
    }

    // ── POST /App/User/ResetPassword ──────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(string id, string newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            TempData["Error"] = "Şifre en az 6 karakter olmalıdır.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        // [G-02] Çapraz-tenant şifre sıfırlama engeli
        if (user.TenantId != _tenantService.TenantId) return NotFound();

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, newPassword);

        if (result.Succeeded)
        {
            await _userManager.UpdateSecurityStampAsync(user);
            TempData["Success"] = $"'{user.FullName}' şifresi sıfırlandı.";
        }
        else
        {
            TempData["Error"] = string.Join(", ", result.Errors.Select(e => e.Description));
        }

        return RedirectToAction(nameof(Edit), new { id });
    }

    // ── POST /App/User/Delete ──────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        // [G-02] Çapraz-tenant silme engeli
        if (user.TenantId != _tenantService.TenantId) return NotFound();

        if (user.Id == _userManager.GetUserId(User))
        {
            TempData["Error"] = "Kendi hesabınızı silemezsiniz.";
            return RedirectToAction(nameof(Index));
        }

        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains("Admin"))
        {
            // [G-02-DEL] Tenant'a özgü admin sayımı — tüm tenantlar değil
            var tenantAdminCount = await (
                from u in _db.Users.Where(u => u.TenantId == _tenantService.TenantId)
                join ur in _db.UserRoles on u.Id equals ur.UserId
                join r in _db.Roles on ur.RoleId equals r.Id
                where r.Name == "Admin"
                select u.Id
            ).CountAsync();

            if (tenantAdminCount <= 1)
            {
                TempData["Error"] = "Sistemde en az bir Admin bulunmalıdır.";
                return RedirectToAction(nameof(Index));
            }
        }

        var hasActiveOrders = await _db.Orders
            .AnyAsync(o => o.OrderOpenedBy == user.FullName && o.OrderStatus == OrderStatus.Open);
        if (hasActiveOrders)
        {
            TempData["Error"] = $"'{user.FullName}' adına açık siparişler var. Önce kapatın.";
            return RedirectToAction(nameof(Index));
        }

        await _userManager.DeleteAsync(user);
        TempData["Success"] = "Kullanıcı silindi.";
        return RedirectToAction(nameof(Index));
    }
}
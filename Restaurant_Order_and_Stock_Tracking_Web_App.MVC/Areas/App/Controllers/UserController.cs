// ============================================================================
//  Controllers/UserController.cs
//  SPRINT 2 — Identity Güvenliği & N+1 Optimizasyonu
//
//  [G-02] Çapraz-tenant kullanıcı erişimi kapatıldı:
//         _userManager.Users → _db.Users + WHERE TenantId filtresi
//         FindByIdAsync sonrası her metotta sahiplik doğrulaması eklendi.
//
//  [G-03] Yeni kullanıcı oluşturulurken TenantId = _tenantService.TenantId
//         artık zorunlu — null TenantId Global Query Filter bypass'ını önler.
//
//  [P-01] foreach + GetRolesAsync N+1 → tek LEFT JOIN sorgusu.
//
//  [G-02-DEL] Delete'teki GetUsersInRoleAsync tüm tenantları sayıyordu;
//             tenant'a özgü admin sayımı _db JOIN sorgusuna taşındı.
//
//  Değişmeyen her satır orijinalle birebir aynıdır.
// ============================================================================

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;       // [G-02/G-03] eklendi
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared.Common;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Users;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.App.Controllers;
[Area("App")]
[Authorize(Roles = "Admin")]
public class UserController : AppBaseController
{
    // ── Sistemde izin verilen roller — tek kaynak gerçek ────────────
    // [SPRINT-2] "Kitchen" rolü eklendi; Kullanıcı oluşturma/düzenleme
    // formlarında dropdown'a otomatik yansır.
    private static readonly string[] AllowedRoles = { "Admin", "Garson", "Kasiyer", "Kitchen" };

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly RestaurantDbContext _db;
    private readonly ITenantService _tenantService; // [G-02/G-03]

    public UserController(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        RestaurantDbContext db,
        ITenantService tenantService)  // [G-02/G-03]
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
        _tenantService = tenantService; // [G-02/G-03]
    }

    // ── GET /User ─────────────────────────────────────────────────────
    // [G-02] ESKİ: _userManager.Users.ToListAsync() → tüm tenant kullanıcıları
    //              + foreach GetRolesAsync → N+1 (50 kullanıcı = 51 sorgu)
    //
    // YENİ: _db.Users + LEFT JOIN _db.UserRoles + _db.Roles → tek sorgu.
    //       WHERE u.TenantId == tenantId → yalnızca bu restoranın kullanıcıları.
    //
    // NOT: ApplicationUser için HasQueryFilter tanımlı değil; bu nedenle
    //      tenant filtresi elle ekleniyor (Identity API'larının tamamında aynı kural).
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Kullanıcı Yönetimi";

        var tenantId = _tenantService.TenantId;

        // [G-02 + P-01] Tek LEFT JOIN — kullanıcı + rolü tek round-trip'te çek.
        var model = await (
            from u in _db.Users
                        .Where(u => u.TenantId == tenantId)     // [G-02] tenant izolasyonu
            join ur in _db.UserRoles on u.Id equals ur.UserId into urs
            from ur in urs.DefaultIfEmpty()                      // LEFT JOIN: rolsüz kullanıcı da gelsin
            join r in _db.Roles on ur.RoleId equals r.Id into rs
            from r in rs.DefaultIfEmpty()                       // LEFT JOIN
            select new UserListItemViewModel
            {
                Id = u.Id,
                UserName = u.UserName ?? string.Empty,
                FullName = u.FullName,
                Email = u.Email,
                PhoneNumber = u.PhoneNumber,
                Role = r != null ? r.Name ?? "— Rol Yok —" : "— Rol Yok —",
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt
            }
        ).ToListAsync(); // [P-01] N+1 → 1 sorgu

        return View(model);
    }

    //// ── GET /User/Create ──────────────────────────────────────────────
    public IActionResult Create()
    {
        ViewData["Title"] = "Yeni Kullanıcı";
        ViewBag.Roles = AllowedRoles; // DB'ye bağımlı değil, her zaman 3 rol
        return View();
    }

    // ── POST /User/Create ─────────────────────────────────────────────
    // ── POST /User/Create ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserCreateViewModel model)
    {
        // 🚨 1. ADIM: TENANT ID GÜVENLİK DUVARI (Senin İstediğin Kısım) 🚨
        var currentTenantId = _tenantService.TenantId;

        if (string.IsNullOrWhiteSpace(currentTenantId))
        {
            // Eğer dükkan ID'si yoksa anında işlemi iptal et ve hata ver!
            TempData["Error"] = "Kritik Hata: Dükkan kimliği (TenantId) bulunamadı. Dükkansız personel oluşturulamaz!";
            return RedirectToAction(nameof(Index));
        }

        // 2. Rol izin listesi dışında bir değer form'dan gönderilmeye çalışılırsa reddet
        if (!string.IsNullOrEmpty(model.Role) && !AllowedRoles.Contains(model.Role))
            ModelState.AddModelError("Role", "Geçersiz rol seçimi.");

        if (!ModelState.IsValid)
        {
            ViewBag.Roles = AllowedRoles;
            return View(model);
        }

        if (await _userManager.FindByNameAsync(model.UserName) != null)
        {
            ModelState.AddModelError("UserName", "Bu kullanıcı adı zaten kullanılıyor.");
            ViewBag.Roles = AllowedRoles;
            return View(model);
        }

        // Rol DB'de yoksa seed edip oluştur (her ortamda güvenli)
        if (!await _roleManager.RoleExistsAsync(model.Role))
            await _roleManager.CreateAsync(new IdentityRole(model.Role));

        var user = new ApplicationUser
        {
            UserName = model.UserName.Trim(),
            FullName = model.FullName.Trim(),
            Email = model.Email?.Trim(),
            PhoneNumber = model.PhoneNumber,
            CreatedAt = DateTime.UtcNow,
            EmailConfirmed = true,

            // 🚨 3. ADIM: Artık TenantId'nin kesinlikle dolu olduğundan eminiz!
            TenantId = currentTenantId
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
            await _userManager.DeleteAsync(user); // işlemi geri al
            foreach (var e in roleResult.Errors)
                ModelState.AddModelError(string.Empty, $"Rol ataması başarısız: {e.Description}");
            ViewBag.Roles = AllowedRoles;
            return View(model);
        }

        TempData["Success"] = $"'{user.FullName}' kullanıcısı '{model.Role}' rolüyle oluşturuldu.";
        return RedirectToAction(nameof(Index));
    }
    // ── GET /User/Edit/{id} ───────────────────────────────────────────
    public async Task<IActionResult> Edit(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        // [G-02] FindByIdAsync Identity store'da global arar; TenantId kontrolü
        //        olmadan başka restoranın kullanıcısı düzenlenebilirdi.
        if (user.TenantId != _tenantService.TenantId) return NotFound();

        var roles = await _userManager.GetRolesAsync(user);
        ViewData["Title"] = "Kullanıcı Düzenle";
        ViewBag.Roles = AllowedRoles;

        if (!roles.Any())
            TempData["Warning"] = "Bu kullanıcıya henüz rol atanmamış! Aşağıdan bir rol seçip kaydedin.";

        return View(new UserEditViewModel
        {
            Id = user.Id,
            UserName = user.UserName?.Trim() ?? "",
            FullName = user.FullName.Trim(),
            Email = user.Email?.Trim(),
            PhoneNumber = user.PhoneNumber,
            Role = roles.FirstOrDefault() ?? ""

            
        });
    }

    // ── POST /User/Edit ───────────────────────────────────────────────
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

        // [G-02] POST ile başka tenant kullanıcısının verisi değiştirilemez.
        if (user.TenantId != _tenantService.TenantId) return NotFound();

        if (user.UserName != model.UserName)
        {
            if (await _userManager.FindByNameAsync(model.UserName) != null)
            {
                ModelState.AddModelError("UserName", "Bu kullanıcı adı zaten kullanılıyor.");
                ViewBag.Roles = AllowedRoles;
                return View(model);
            }
        }

        user.UserName = model.UserName;
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

    // ── POST /User/ResetPassword ──────────────────────────────────────
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

        // [G-02] Çapraz-tenant şifre sıfırlama engellendi.
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

    // ── POST /User/Delete ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        // [G-02] Çapraz-tenant silme engellendi.
        if (user.TenantId != _tenantService.TenantId) return NotFound();

        if (user.Id == _userManager.GetUserId(User))
        {
            TempData["Error"] = "Kendi hesabınızı silemezsiniz.";
            return RedirectToAction(nameof(Index));
        }

        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains("Admin"))
        {
            // [G-02-DEL] ESKİ: GetUsersInRoleAsync("Admin") tüm tenantlardaki
            //            admin sayısını döndürür → başka restoranın admin'i sayılıyor.
            //
            // YENİ: _db JOIN ile sadece bu tenant'ın admin sayısı sorgulanır.
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
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared.Common;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Users;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Controllers;

[Authorize(Roles = "Admin")]
public class UserController : Controller
{
    // ── Sistemde izin verilen roller — tek kaynak gerçek ────────────
    // [SPRINT-2] "Kitchen" rolü eklendi; Kullanıcı oluşturma/düzenleme
    // formlarında dropdown'a otomatik yansır.
    private static readonly string[] AllowedRoles = { "Admin", "Garson", "Kasiyer", "Kitchen" };

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly RestaurantDbContext _db;

    public UserController(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        RestaurantDbContext db)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
    }

    // ── GET /User ─────────────────────────────────────────────────────
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Kullanıcı Yönetimi";

        var users = await _userManager.Users.ToListAsync();
        var model = new List<UserListItemViewModel>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            model.Add(new UserListItemViewModel
            {
                Id = user.Id,
                UserName = user.UserName ?? "",
                FullName = user.FullName,
                Email = user.Email,
                PhoneNumber = user.PhoneNumber,
                Role = roles.FirstOrDefault() ?? "— Rol Yok —",
                CreatedAt = user.CreatedAt,
                LastLoginAt = user.LastLoginAt
            });
        }

        return View(model);
    }

    // ── GET /User/Create ──────────────────────────────────────────────
    public IActionResult Create()
    {
        ViewData["Title"] = "Yeni Kullanıcı";
        ViewBag.Roles = AllowedRoles; // DB'ye bağımlı değil, her zaman 3 rol
        return View();
    }

    // ── POST /User/Create ─────────────────────────────────────────────
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserCreateViewModel model)
    {
        // Rol izin listesi dışında bir değer form'dan gönderilmeye çalışılırsa reddet
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
            UserName = model.UserName,
            FullName = model.FullName,
            Email = model.Email,
            PhoneNumber = model.PhoneNumber,
            CreatedAt = DateTime.UtcNow,
            EmailConfirmed = true
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

        var roles = await _userManager.GetRolesAsync(user);
        ViewData["Title"] = "Kullanıcı Düzenle";
        ViewBag.Roles = AllowedRoles;

        if (!roles.Any())
            TempData["Warning"] = "Bu kullanıcıya henüz rol atanmamış! Aşağıdan bir rol seçip kaydedin.";

        return View(new UserEditViewModel
        {
            Id = user.Id,
            UserName = user.UserName ?? "",
            FullName = user.FullName,
            Email = user.Email,
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

        if (user.Id == _userManager.GetUserId(User))
        {
            TempData["Error"] = "Kendi hesabınızı silemezsiniz.";
            return RedirectToAction(nameof(Index));
        }

        var roles = await _userManager.GetRolesAsync(user);
        if (roles.Contains("Admin"))
        {
            var adminCount = (await _userManager.GetUsersInRoleAsync("Admin")).Count;
            if (adminCount <= 1)
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
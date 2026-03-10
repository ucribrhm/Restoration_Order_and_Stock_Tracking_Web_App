using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Dtos.Menu;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using System.Globalization;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.App.Controllers
{
    [Area("App")]
    [Authorize(Roles = "Admin")]
    public class MenuController : AppBaseController
    {
        private readonly RestaurantDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly ITenantService _tenantService;

        private static readonly HashSet<string> _allowedExtensions =
            new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
        private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB

        public MenuController(RestaurantDbContext context, IWebHostEnvironment env,
            ITenantService tenantService)
        {
            _context = context;
            _env = env;
            _tenantService = tenantService;
        }

        // ── GET: /Menu ──────────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Menü Ürünleri";

            var menuItems = await _context.MenuItems
                .Where(m => !m.IsDeleted)
                .Include(m => m.Category)
                .OrderBy(m => m.Category.CategorySortOrder)
                .ThenBy(m => m.DisplayOrder)
                .ThenBy(m => m.MenuItemName)
                .ToListAsync();

            ViewData["Categories"] = await _context.Categories
                .Where(c => c.IsActive)
                .OrderBy(c => c.CategorySortOrder)
                .ThenBy(c => c.CategoryName)
                .ToListAsync();

            ViewData["HasLowStock"] = await _context.MenuItems
                .AnyAsync(m => !m.IsDeleted && m.TrackStock && m.StockQuantity < 5);

            return View(menuItems);
        }

        // ── GET: /Menu/Detail/5 ─────────────────────────────────────────
        public async Task<IActionResult> Detail(int id)
        {
            var item = await _context.MenuItems
                .Include(m => m.Category)
                .FirstOrDefaultAsync(m => m.MenuItemId == id);
            if (item == null) return NotFound();

            ViewData["Title"] = $"{item.MenuItemName} — Detay";
            ViewData["HasLowStock"] = await _context.MenuItems
                .AnyAsync(m => !m.IsDeleted && m.TrackStock && m.StockQuantity < 5);
            return View(item);
        }

        // ── GET: /Menu/Create ───────────────────────────────────────────
        public async Task<IActionResult> Create()
        {
            ViewData["Title"] = "Yeni Ürün";
            ViewData["Categories"] = await _context.Categories
                .Where(c => c.IsActive)
                .OrderBy(c => c.CategorySortOrder)
                .ToListAsync();
            return View();
        }

        // ── POST: /Menu/Create  (AJAX — multipart/form-data) ────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromForm] MenuItemCreateDto dto)
        {
            // Fiyat parse — virgül/nokta her ikisini de kabul eder
            if (!decimal.TryParse(
                    dto.MenuItemPriceStr?.Replace(',', '.'),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out decimal menuItemPrice) || menuItemPrice < 0)
                return Json(new { success = false, message = "Geçersiz fiyat değeri." });

            bool catExists = await _context.Categories.AnyAsync(c => c.CategoryId == dto.CategoryId);
            if (!catExists)
                return Json(new { success = false, message = "Seçilen kategori bulunamadı." });

            // Görsel yükleme
            string? imagePath = null;
            if (dto.ImageFile != null && dto.ImageFile.Length > 0)
            {
                var (path, err) = await SaveImageAsync(dto.ImageFile);
                if (err != null) return Json(new { success = false, message = err });
                imagePath = path;
            }

            var item = new MenuItem
            {
                MenuItemName = dto.MenuItemName.Trim(),
                NameEn = string.IsNullOrWhiteSpace(dto.NameEn) ? null : dto.NameEn.Trim(),
                NameAr = string.IsNullOrWhiteSpace(dto.NameAr) ? null : dto.NameAr.Trim(),
                NameRu = string.IsNullOrWhiteSpace(dto.NameRu) ? null : dto.NameRu.Trim(),
                CategoryId = dto.CategoryId,
                MenuItemPrice = menuItemPrice,
                Description = dto.Description?.Trim() ?? string.Empty,
                DescriptionEn = string.IsNullOrWhiteSpace(dto.DescriptionEn) ? null : dto.DescriptionEn.Trim(),
                DescriptionAr = string.IsNullOrWhiteSpace(dto.DescriptionAr) ? null : dto.DescriptionAr.Trim(),
                DescriptionRu = string.IsNullOrWhiteSpace(dto.DescriptionRu) ? null : dto.DescriptionRu.Trim(),
                DetailedDescription = string.IsNullOrWhiteSpace(dto.DetailedDescription) ? null : dto.DetailedDescription.Trim(),
                StockQuantity = dto.StockQuantity,
                TrackStock = dto.TrackStock,
                IsAvailable = dto.IsAvailable,
                DisplayOrder = dto.DisplayOrder,
                IsDeleted = false,
                ImagePath = imagePath,
                MenuItemCreatedTime = DateTime.UtcNow,
                TenantId = _tenantService.TenantId!
            };

            _context.MenuItems.Add(item);
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Ürün başarıyla eklendi." });
        }

        // ── GET: /Menu/Edit/5 ───────────────────────────────────────────
        public async Task<IActionResult> Edit(int id)
        {
            var item = await _context.MenuItems
                .Include(m => m.Category)
                .FirstOrDefaultAsync(m => m.MenuItemId == id);
            if (item == null) return NotFound();

            ViewData["Title"] = $"{item.MenuItemName} — Düzenle";
            ViewData["Categories"] = await _context.Categories
                .Where(c => c.IsActive)
                .OrderBy(c => c.CategorySortOrder)
                .ToListAsync();
            return View(item);
        }

        // ── POST: /Menu/Edit  (AJAX — multipart/form-data) ──────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit([FromForm] MenuItemEditDto dto)
        {
            var item = await _context.MenuItems.FirstOrDefaultAsync(m => m.MenuItemId == dto.Id); // [G-01] FindAsync → FirstOrDefaultAsync
            if (item == null)
                return Json(new { success = false, message = "Ürün bulunamadı." });

            // Fiyat parse
            if (!decimal.TryParse(
                    dto.MenuItemPriceStr?.Replace(',', '.'),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out decimal menuItemPrice) || menuItemPrice < 0)
                return Json(new { success = false, message = "Geçersiz fiyat değeri." });

            // Görsel işlemleri
            if (dto.RemoveImage && item.ImagePath != null)
            {
                DeleteImageFile(item.ImagePath);
                item.ImagePath = null;
            }
            else if (dto.ImageFile != null && dto.ImageFile.Length > 0)
            {
                // Yeni görsel yüklendi — eskiyi sil, yerine kaydet
                if (item.ImagePath != null) DeleteImageFile(item.ImagePath);
                var (path, err) = await SaveImageAsync(dto.ImageFile);
                if (err != null) return Json(new { success = false, message = err });
                item.ImagePath = path;
            }

            // Alanları güncelle
            item.MenuItemName = dto.MenuItemName.Trim();
            item.NameEn = string.IsNullOrWhiteSpace(dto.NameEn) ? null : dto.NameEn.Trim();
            item.NameAr = string.IsNullOrWhiteSpace(dto.NameAr) ? null : dto.NameAr.Trim();
            item.NameRu = string.IsNullOrWhiteSpace(dto.NameRu) ? null : dto.NameRu.Trim();
            item.CategoryId = dto.CategoryId;
            item.MenuItemPrice = menuItemPrice;
            item.Description = dto.Description?.Trim() ?? string.Empty;
            item.DescriptionEn = string.IsNullOrWhiteSpace(dto.DescriptionEn) ? null : dto.DescriptionEn.Trim();
            item.DescriptionAr = string.IsNullOrWhiteSpace(dto.DescriptionAr) ? null : dto.DescriptionAr.Trim();
            item.DescriptionRu = string.IsNullOrWhiteSpace(dto.DescriptionRu) ? null : dto.DescriptionRu.Trim();
            item.DetailedDescription = string.IsNullOrWhiteSpace(dto.DetailedDescription) ? null : dto.DetailedDescription.Trim();
            item.StockQuantity = dto.StockQuantity;
            item.TrackStock = dto.TrackStock;
            item.IsAvailable = dto.IsAvailable;
            item.DisplayOrder = dto.DisplayOrder;

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Ürün başarıyla güncellendi." });
        }

        // ── POST: /Menu/Delete ──────────────────────────────────────────
        // Siparişlerde kullanıldıysa → soft delete; hiç kullanılmadıysa → fiziksel sil
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var item = await _context.MenuItems.FirstOrDefaultAsync(m => m.MenuItemId == id); // [G-01] FindAsync → FirstOrDefaultAsync
            if (item == null) return Json(new { success = false, message = "Ürün bulunamadı." });

            bool usedInOrders = await _context.OrderItems.AnyAsync(oi => oi.MenuItemId == id);
            if (usedInOrders)
            {
                item.IsDeleted = true;
                item.IsAvailable = false;
                await _context.SaveChangesAsync();
                return Json(new { success = true, message = "Ürün pasife alındı (geçmiş siparişlerde kullanılmış)." });
            }

            // Fiziksel sil — görseli de temizle
            if (item.ImagePath != null) DeleteImageFile(item.ImagePath);
            _context.MenuItems.Remove(item);
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Ürün silindi." });
        }

        // ── GET: /Menu/GetById/5  (Edit modal için AJAX) ────────────────
        [HttpGet]
        public async Task<IActionResult> GetById(int id)
        {
            var m = await _context.MenuItems.FirstOrDefaultAsync(m => m.MenuItemId == id); // [G-01] FindAsync → FirstOrDefaultAsync
            if (m == null) return Json(new { success = false });

            return Json(new
            {
                success = true,
                menuItemId = m.MenuItemId,
                menuItemName = m.MenuItemName,
                nameEn = m.NameEn ?? "",
                nameAr = m.NameAr ?? "",
                nameRu = m.NameRu ?? "",
                categoryId = m.CategoryId,
                menuItemPrice = m.MenuItemPrice.ToString("F2", CultureInfo.InvariantCulture),
                description = m.Description ?? "",
                descriptionEn = m.DescriptionEn ?? "",
                descriptionAr = m.DescriptionAr ?? "",
                descriptionRu = m.DescriptionRu ?? "",
                detailedDescription = m.DetailedDescription ?? "",
                stockQuantity = m.StockQuantity,
                trackStock = m.TrackStock,
                isAvailable = m.IsAvailable,
                displayOrder = m.DisplayOrder,
                imagePath = m.ImagePath
            });
        }

        // ── Yardımcı: Görsel kaydet ─────────────────────────────────────
        private async Task<(string? path, string? error)> SaveImageAsync(IFormFile file)
        {
            var ext = Path.GetExtension(file.FileName);
            if (!_allowedExtensions.Contains(ext))
                return (null, "Geçersiz dosya türü. Yalnızca JPG, PNG, WEBP veya GIF yüklenebilir.");
            if (file.Length > MaxFileSizeBytes)
                return (null, "Dosya boyutu 5 MB'ı geçemez.");

            var folder = Path.Combine(_env.WebRootPath, "images", "menu");
            Directory.CreateDirectory(folder);

            var fileName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
            var fullPath = Path.Combine(folder, fileName);
            await using var stream = new FileStream(fullPath, FileMode.Create);
            await file.CopyToAsync(stream);
            return ($"/images/menu/{fileName}", null);
        }

        // ── Yardımcı: Görsel sil ────────────────────────────────────────
        private void DeleteImageFile(string relativePath)
        {
            try
            {
                var full = Path.Combine(_env.WebRootPath, relativePath.TrimStart('/'));
                if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
            }
            catch { /* Dosya yoksa veya silinemiyorsa sessizce geç */ }
        }
    }
}
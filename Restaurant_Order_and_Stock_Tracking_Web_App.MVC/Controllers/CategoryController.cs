using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Dtos.Category;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Controllers
{
    [Authorize(Roles = "Admin")]
    public class CategoryController : Controller
    {
        private readonly RestaurantDbContext _context;
        private readonly ITenantService _tenantService;

        public CategoryController(RestaurantDbContext context,
            ITenantService tenantService)
        {
            _context = context;
            _tenantService = tenantService;
        }

        // ── GET: /Category ───────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Kategoriler";

            var categories = await _context.Categories
                .Include(c => c.MenuItems)
                .OrderBy(c => c.CategorySortOrder)
                .ThenBy(c => c.CategoryName)
                .ToListAsync();

            ViewData["HasLowStock"] = await _context.MenuItems
                .AnyAsync(m => m.TrackStock && m.StockQuantity < 5);

            return View(categories);
        }

        // ── GET: /Category/Detail/5 ──────────────────────────────────────
        public async Task<IActionResult> Detail(int id)
        {
            var category = await _context.Categories
                .Include(c => c.MenuItems)
                .FirstOrDefaultAsync(c => c.CategoryId == id);

            if (category == null) return NotFound();

            ViewData["Title"] = $"{category.CategoryName} — Detay";
            ViewData["HasLowStock"] = await _context.MenuItems
                .AnyAsync(m => m.TrackStock && m.StockQuantity < 5);

            return View(category);
        }

        // ── GET: /Category/Create ────────────────────────────────────────
        public IActionResult Create()
        {
            ViewData["Title"] = "Yeni Kategori";
            return View();
        }

        // ── POST: /Category/Create  (AJAX JSON) ─────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] CategoryCreateDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.CategoryName))
                return Json(new { success = false, message = "Kategori adı boş olamaz." });

            bool exists = await _context.Categories
                .AnyAsync(c => c.CategoryName.ToLower() == dto.CategoryName.Trim().ToLower());

            if (exists)
                return Json(new { success = false, message = "Bu kategori adı zaten mevcut." });

            var category = new Category
            {
                CategoryName = dto.CategoryName.Trim(),
                TenantId = _tenantService.TenantId!,
                NameEn = string.IsNullOrWhiteSpace(dto.NameEn) ? null : dto.NameEn.Trim(),
                NameAr = string.IsNullOrWhiteSpace(dto.NameAr) ? null : dto.NameAr.Trim(),
                NameRu = string.IsNullOrWhiteSpace(dto.NameRu) ? null : dto.NameRu.Trim(),
                CategorySortOrder = dto.CategorySortOrder,
                IsActive = dto.IsActive
            };

            _context.Categories.Add(category);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Kategori başarıyla eklendi." });
        }

        // ── GET: /Category/Edit/5 ────────────────────────────────────────
        public async Task<IActionResult> Edit(int id)
        {
            var category = await _context.Categories.FindAsync(id);
            if (category == null) return NotFound();

            ViewData["Title"] = "Kategori Düzenle";
            return View(category);
        }

        // ── POST: /Category/Edit  (AJAX JSON) ───────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit([FromBody] CategoryEditDto dto)
        {
            var category = await _context.Categories.FindAsync(dto.Id);
            if (category == null)
                return Json(new { success = false, message = "Kategori bulunamadı." });

            if (string.IsNullOrWhiteSpace(dto.CategoryName))
                return Json(new { success = false, message = "Kategori adı boş olamaz." });

            bool exists = await _context.Categories
                .AnyAsync(c => c.CategoryName.ToLower() == dto.CategoryName.Trim().ToLower()
                            && c.CategoryId != dto.Id);

            if (exists)
                return Json(new { success = false, message = "Bu kategori adı zaten mevcut." });

            category.CategoryName = dto.CategoryName.Trim();
            category.NameEn = string.IsNullOrWhiteSpace(dto.NameEn) ? null : dto.NameEn.Trim();
            category.NameAr = string.IsNullOrWhiteSpace(dto.NameAr) ? null : dto.NameAr.Trim();
            category.NameRu = string.IsNullOrWhiteSpace(dto.NameRu) ? null : dto.NameRu.Trim();
            category.CategorySortOrder = dto.CategorySortOrder;
            category.IsActive = dto.IsActive;

            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Kategori güncellendi." });
        }

        // ── POST: /Category/Delete  (AJAX JSON) ─────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var category = await _context.Categories
                .Include(c => c.MenuItems)
                .FirstOrDefaultAsync(c => c.CategoryId == id);

            if (category == null)
                return Json(new { success = false, message = "Kategori bulunamadı." });

            if (category.MenuItems != null && category.MenuItems.Any())
                return Json(new
                {
                    success = false,
                    message = $"Bu kategoriye bağlı {category.MenuItems.Count} ürün var. Önce ürünleri silin veya başka kategoriye taşıyın."
                });

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Kategori silindi." });
        }

        // ── GET: /Category/GetById/5 ─────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetById(int id)
        {
            var c = await _context.Categories.FindAsync(id);
            if (c == null) return Json(new { success = false });

            return Json(new
            {
                success = true,
                categoryId = c.CategoryId,
                categoryName = c.CategoryName,
                nameEn = c.NameEn ?? "",
                nameAr = c.NameAr ?? "",
                nameRu = c.NameRu ?? "",
                categorySortOrder = c.CategorySortOrder,
                isActive = c.IsActive
            });
        }
    }
}
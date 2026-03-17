using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Filters;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Controllers
{
    [AllowAnonymous] // QR Menü herkese açıktır
   
    public class QrMenuController : Controller
    {
        private readonly RestaurantDbContext _context;
        private readonly IHubContext<RestaurantHub> _hub;
        private readonly ILogger<QrMenuController> _logger;

        private static readonly HashSet<string> _validLangs =
            new(StringComparer.OrdinalIgnoreCase) { "tr", "en", "ar", "ru" };

        public QrMenuController(
            RestaurantDbContext context,
            IHubContext<RestaurantHub> hub,
            ILogger<QrMenuController> logger)
        {
            _context = context;
            _hub = hub;
            _logger = logger;
        }

        // ── GET /QrMenu/{tenantId}/Index/{tableName} ──────────────────────────────
        // [YENİ] URL'ye tenantId eklendi!
        // [SEC-RL-3] QrMenuPolicy: 60 saniyede 30 istek — scraping/DDoS koruması.
        [HttpGet]
        [EnableRateLimiting("QrMenuPolicy")]
        [Route("QrMenu/{tenantId}/Index/{tableName}")]
        public async Task<IActionResult> Index(string tenantId, string tableName)
        {
            var decodedName = Uri.UnescapeDataString(tableName);

            // [YENİ] IgnoreQueryFilters() şart! Çünkü kullanıcı giriş yapmadığı için otomatik filtre çalışmaz.
            // Sadece bu URL'deki tenantId'ye ait masayı arıyoruz.
            var table = await _context.Tables
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.TableName == decodedName);

            if (table == null)
                return NotFound("Bu QR koda ait masa bulunamadı veya dükkan kapalı.");

            // Dil Seçimi
            string lang = "tr";
            if (!string.IsNullOrWhiteSpace(Request.Query["lang"]))
            {
                var qLang = Request.Query["lang"].ToString().ToLower();
                if (_validLangs.Contains(qLang)) lang = qLang;
            }
            else if (Request.Cookies.TryGetValue("qrmenu_lang", out var cookieLang)
                     && _validLangs.Contains(cookieLang?.ToLower() ?? ""))
            {
                lang = cookieLang!.ToLower();
            }

            Response.Cookies.Append("qrmenu_lang", lang, new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(30),
                HttpOnly = false,
                SameSite = SameSiteMode.Lax
            });

            // [YENİ] Sadece bu dükkana (tenantId) ait kategorileri ve ürünleri çek
            var categories = await _context.Categories
                .IgnoreQueryFilters()
                .Where(c => c.TenantId == tenantId && c.IsActive)
                .OrderBy(c => c.CategorySortOrder)
                .Include(c => c.MenuItems
                    .Where(m =>
                        !m.IsDeleted &&
                        (m.IsAvailable || (m.TrackStock && m.StockQuantity > 0))
                    )
                    .OrderBy(m => m.MenuItemCreatedTime)
                )
                .ToListAsync();

            categories = categories
                .Where(c => c.MenuItems != null && c.MenuItems.Any())
                .ToList();

            ViewData["Title"] = $"{table.TableName} — Menü";
            ViewData["TableName"] = table.TableName;
            ViewData["TenantId"] = tenantId; // [YENİ] JS ve View linkleri için şart!
            ViewData["IsWaiterCalled"] = table.IsWaiterCalled;
            ViewData["Lang"] = lang;

            return View(categories);
        }

        // ── GET /QrMenu/{tenantId}/Detail/{id}?tableName={tableName} ─────────────────────
        // [SEC-RL-3] QrMenuPolicy: Index ile aynı policy — ürün detay scraping koruması.
        [HttpGet]
        [EnableRateLimiting("QrMenuPolicy")]
        [Route("QrMenu/{tenantId}/Detail/{id}")]
        public async Task<IActionResult> Detail(string tenantId, int id, string? tableName)
        {
            var item = await _context.MenuItems
                .IgnoreQueryFilters()
                .Include(m => m.Category)
                .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.MenuItemId == id && !m.IsDeleted);

            if (item == null)
                return NotFound("Ürün bulunamadı.");

            var sibling = await _context.MenuItems
                .IgnoreQueryFilters()
                .Where(m => m.TenantId == tenantId && !m.IsDeleted && m.CategoryId == item.CategoryId
                    && (m.IsAvailable || (m.TrackStock && m.StockQuantity > 0)))
                .OrderBy(m => m.MenuItemCreatedTime)
                .Select(m => new { m.MenuItemId, m.MenuItemName })
                .ToListAsync();

            var currentIdx = sibling.FindIndex(m => m.MenuItemId == id);
            ViewData["PrevItemId"] = currentIdx > 0 ? sibling[currentIdx - 1].MenuItemId : (int?)null;
            ViewData["NextItemId"] = currentIdx < sibling.Count - 1 ? sibling[currentIdx + 1].MenuItemId : (int?)null;

            ViewData["Title"] = item.MenuItemName;
            ViewData["TableName"] = tableName ?? "";
            ViewData["TenantId"] = tenantId; // İleri/Geri linkleri için lazım

            return View(item);
        }

        [HttpPost]
        [IgnoreAntiforgeryToken]
        [EnableRateLimiting("WaiterCallPolicy")]
        [Route("QrMenu/CallWaiter")]
        public async Task<IActionResult> CallWaiter([FromBody] CallWaiterRequest request)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.TableName) || string.IsNullOrWhiteSpace(request.TenantId))
                return BadRequest(new { success = false, message = "Geçersiz masa veya dükkan bilgisi." });

            var table = await _context.Tables
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.TenantId == request.TenantId && t.TableName == request.TableName);

            if (table == null)
                return NotFound(new { success = false, message = "Masa bulunamadı." });

            if (table.IsWaiterCalled)
                return Ok(new { success = true, alreadyCalled = true, message = "Garson zaten çağrıldı." });

            table.IsWaiterCalled = true;
            table.WaiterCalledAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // 🚀 GARANTİLİ MESAJ GÖNDERİMİ VE LOGLAMA
            // Veritabanındaki TenantId'nin sağında solunda boşluk kalmışsa diye Trimliyoruz!
            var roomId = table.TenantId?.Trim() ?? "";

            if (!string.IsNullOrEmpty(roomId))
            {
                await _hub.Clients.Group(roomId).SendAsync("WaiterCalled", new
                {
                    tableName = table.TableName,
                    calledAtUtc = table.WaiterCalledAt!.Value.ToString("o")
                });
                // [SEC-3] Console.WriteLine → ILogger.LogDebug
                // Production'da LogLevel.Debug kapalıysa sıfır overhead.
                _logger.LogDebug(
                    "[QrMenu] {RoomId} odasina WaiterCalled mesaji gonderildi. Masa: {TableName}",
                    roomId, table.TableName);
            }

            return Ok(new { success = true, alreadyCalled = false, message = "Garson çağrıldı." });

        }

        // [YENİ] JSON Payload'una TenantId eklendi!
    }
    public class CallWaiterRequest
    {
        public string TenantId { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
    }
}
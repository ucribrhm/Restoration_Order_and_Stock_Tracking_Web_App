using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Filters;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared.Common;
using System.Security.Claims;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.App.Controllers
{
    [Area("App")]
    [AllowAnonymous]
    public class KitchenController : Controller
    {
        private readonly RestaurantDbContext _db;
        private readonly IHubContext<RestaurantHub> _hub;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<KitchenController> _logger;
        private readonly ITenantFeatureService _featureService;

        public KitchenController(
            RestaurantDbContext db,
            IHubContext<RestaurantHub> hub,
            IWebHostEnvironment env,
            ILogger<KitchenController> logger,
            ITenantFeatureService featureService)
        {
            _db = db;
            _hub = hub;
            _env = env;
            _logger = logger;
            _featureService = featureService;
        }

        // ── 1. GÜÇLENDİRİLMİŞ TENANT ÇÖZÜMLEME ZIRHI ──
        private string? ResolveTenantId(string? queryStringFallback = null)
        {
            // KADEME 1: Claims (Eğer Admin veya Garson kendi hesabından KDS'ye girdiyse)
            var fromClaims = User.FindFirst("TenantId")?.Value
                          ?? User.Claims.FirstOrDefault(c => c.Type.Contains("TenantId", StringComparison.OrdinalIgnoreCase))?.Value;
            if (!string.IsNullOrWhiteSpace(fromClaims)) return fromClaims;

            // KADEME 2: QueryString (URL'den veya parametreden geldiyse - İlk Kurulum)
            if (!string.IsNullOrWhiteSpace(queryStringFallback)) return queryStringFallback;
            var fromQuery = Request.Query["tenantId"].ToString();
            if (!string.IsNullOrWhiteSpace(fromQuery)) return fromQuery;

            // KADEME 3: COOKIE (İŞTE EKSİK OLAN BUYDU! Polling istekleri buraya düşecek)
            var fromCookie = Request.Cookies["ros-tenant"];
            if (!string.IsNullOrWhiteSpace(fromCookie)) return fromCookie;

            return null;
        }

        // ── 2. GÜNCELLENMİŞ DISPLAY METODU ──
        // [ARCH-FIX] [RequireFeature] attribute'u KALDIRILDI.
        // Sebep: [AllowAnonymous] controller'larda filter pipeline'ı
        //        Claims'te TenantId bulamaz → erken false döner → Upgrade'e yönlendirir.
        // Çözüm: TenantId resolvedId olarak bulunduktan SONRA manuel kontrol.
        [HttpGet]
        public async Task<IActionResult> Display(string? tenantId = null)
        {
            // 4 Kademeli zırh ile TenantId'yi bul
            var resolvedId = ResolveTenantId(tenantId);

            // KADEME 4: DB Fallback (Sistemde her şey patlarsa son çare açık siparişten bul)
            // Not: Multi-tenant sistemlerde riskli olabilir ama "hiçlikten" iyidir.
            if (string.IsNullOrEmpty(resolvedId))
            {
                var firstOpenOrder = await _db.Orders.IgnoreQueryFilters().FirstOrDefaultAsync(o => o.OrderStatus == OrderStatus.Open);
                if (firstOpenOrder != null)
                {
                    resolvedId = firstOpenOrder.TenantId;
                    _logger.LogWarning($"[KDS] TenantId DB'deki ilk açık siparişten ({resolvedId}) kurtarıldı!");
                }
            }

            // Hiçbiri yoksa (Veritabanı da boşsa) log bas ve boş dön
            if (string.IsNullOrEmpty(resolvedId))
            {
                _logger.LogWarning("[KDS] Display() — TenantId hiçbir kaynaktan çözümlenemedi. Boş ekran döndürülüyor.");
                return View(Enumerable.Empty<Order>());
            }

            // ── [ARCH-FIX] Manuel KDS Yetki Kontrolü ──────────────────────────
            // TenantId resolvedId olarak BULUNDUKTAN SONRA kontrol yapılır.
            // Bu sayede Cookie/QueryString/Claims fark etmeksizin doğru tenant
            // bilgisiyle KDS erişim yetkisi sorgulanır.
            var hasKdsAccess = await _featureService.IsEnabledAsync(resolvedId, Features.KDS);
            if (!hasKdsAccess)
            {
                // Hayalet Cookie (Ghost Cookie) Temizliği:
                // Tarayıcıda kalmış eski/geçersiz 'ros-tenant' cookie'si
                // yetki bulamama döngüsüne sokabilir. Redirect öncesi imha et.
                _logger.LogWarning(
                    "[KDS] Yetkisiz Erişim veya ÖLÜ TENANT! Tenant: {TenantId}. Cookie siliniyor...",
                    resolvedId);
                Response.Cookies.Delete("ros-tenant");
                return RedirectToAction("Upgrade", "Subscription", new { area = "App" });
            }

            // 🚀 COOKIE TAZELEME: Bulduğumuz TenantId'yi tarayıcıya tekrar mühürle
            Response.Cookies.Append("ros-tenant", resolvedId, new CookieOptions
            {
                HttpOnly = false,                                // SignalR JS için zorunlu — değiştirme!
                Secure = !_env.IsDevelopment(),               // Prod: HTTPS only | Dev: HTTP geçer
                SameSite = SameSiteMode.Lax,                    // QR cross-site uyumluluğu
                Expires = DateTimeOffset.UtcNow.AddDays(30),  // 30 gün kalıcı (eski: 365 gün)
                Path = "/"                                  // Tüm path'lerde geçerli
            });

            // JS ve SignalR için ViewData'ya gönder
            ViewData["TenantId"] = resolvedId;

            // --- Siparişleri Çekme ---
            var orders = await _db.Orders
                .IgnoreQueryFilters()
                .Where(o => o.TenantId == resolvedId && o.OrderStatus == OrderStatus.Open)
                .Include(o => o.Table)
                .Include(o => o.OrderItems.Where(oi => oi.OrderItemStatus == OrderItemStatus.Pending || oi.OrderItemStatus == OrderItemStatus.Preparing))
                    .ThenInclude(oi => oi.MenuItem)
                .OrderBy(o => o.OrderOpenedAt)
                .ToListAsync();

            orders = orders.Where(o => o.OrderItems.Any()).ToList();

            return View(orders);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus([FromBody] KdsStatusUpdateDto dto)
        {
            if (dto is null) return BadRequest(new { message = "Geçersiz istek gövdesi." });

            var resolvedTenantId = ResolveTenantId();
            if (string.IsNullOrEmpty(resolvedTenantId)) return Unauthorized(new { message = "Tenant kimliği belirlenemedi." });

            var item = await _db.OrderItems
                .IgnoreQueryFilters()
                .Include(oi => oi.Order).ThenInclude(o => o.Table)
                .Include(oi => oi.MenuItem)
                .FirstOrDefaultAsync(oi => oi.OrderItemId == dto.OrderItemId);

            if (item is null) return NotFound(new { message = "Sipariş kalemi bulunamadı." });

            if (item.Order?.TenantId != resolvedTenantId) return StatusCode(403, new { message = "Yetkiniz yok." });

            if (!Enum.TryParse<OrderItemStatus>(dto.NewStatus, ignoreCase: true, out var parsedNew))
                return BadRequest(new { message = "Geçersiz durum." });

            bool gecerli = (item.OrderItemStatus == OrderItemStatus.Pending && parsedNew == OrderItemStatus.Preparing) ||
                           (item.OrderItemStatus == OrderItemStatus.Preparing && parsedNew == OrderItemStatus.Ready);

            if (!gecerli) return BadRequest(new { message = "Geçersiz geçiş." });

            var tableName = item.Order?.Table?.TableName ?? $"Adisyon #{item.OrderId}";
            var menuItemName = item.MenuItem?.MenuItemName ?? "Ürün";

            item.OrderItemStatus = parsedNew;
            await _db.SaveChangesAsync();

            try
            {
                await _hub.Clients.Group(resolvedTenantId).SendAsync("OrderItemStatusChanged", new
                {
                    orderItemId = item.OrderItemId,
                    newStatus = dto.NewStatus,
                    tableName,
                    menuItemName
                });
            }
            catch (Exception ex) { _logger.LogError(ex, "SignalR Error"); }

            if (parsedNew == OrderItemStatus.Ready)
            {
                try
                {
                    await _hub.Clients.Group(resolvedTenantId).SendAsync("OrderReadyForPickup", new
                    {
                        orderItemId = item.OrderItemId,
                        orderId = item.OrderId,
                        tableId = item.Order?.TableId, // 🚨 EKSİK TABLE ID EKLENDİ! 🚨
                        tableName,
                        menuItemName,
                        readyAt = DateTime.Now.ToString("HH:mm:ss")
                    });
                }
                catch (Exception ex) { _logger.LogError(ex, "SignalR Error"); }
            }

            return Ok(new { success = true, tableName, menuItemName });
        }
    }

    public class KdsStatusUpdateDto
    {
        public int OrderItemId { get; set; }
        public string NewStatus { get; set; } = string.Empty;
    }
}
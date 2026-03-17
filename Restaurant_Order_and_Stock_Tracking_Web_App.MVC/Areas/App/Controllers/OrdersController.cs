// ════════════════════════════════════════════════════════════════════════════
//  Areas/App/Controllers/OrdersController.cs
//  Yol: Areas/App/Controllers/OrdersController.cs
//
//  SPRINT 5 — Areas Refactoring:
//  [S5-NS]   Namespace → ...Areas.App.Controllers
//  [S5-BASE] Controller → AppBaseController ([Area("App")] + AppAuth miras alındı)
//  [S5-URL]  Tüm cross-controller Url.Action / RedirectToAction çağrılarına
//            new { area = "App" } eklendi. Aynı controller içi yönlendirmeler
//            (nameof(Index), nameof(Detail)) area parametresi gerektirmez.
//
//  SPRINT 1 — Thin Controller:
//  Tüm yazma iş mantığı OrderService'e taşınmış; controller sadece
//  HTTP → Service → JSON köprüsü görevini üstlenir.
//
//  SPRINT A — [SA-3] ID Manipulation Koruması:
//  Detail() metoduna Global Query Filter'a ek olarak ikinci savunma hattı
//  eklendi. Sorgu sonrası order.TenantId != _tenantService.TenantId kontrolü
//  yapılır. Bu kontrol Global Query Filter'ın herhangi bir edge case'de
//  (TenantId null, seed bağlamı vb.) devre dışı kalması durumunda da
//  çapraz-tenant sipariş erişimini tamamen engeller.
// ════════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Dtos.Orders;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Filters;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Modules.Orders;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared.Common;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.App.Controllers
{
    [Authorize(Roles = "Admin,Garson,Kasiyer")]
    public class OrdersController : AppBaseController
    {
        // Okuma sorguları için DbContext; yazma işlemleri OrderService üstlenir
        private readonly RestaurantDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IOrderService _orderService;
        private readonly ITenantService _tenantService; // [SA-3]

        public OrdersController(
            RestaurantDbContext db,
            UserManager<ApplicationUser> userManager,
            IOrderService orderService,
            ITenantService tenantService) // [SA-3]
        {
            _db = db;
            _userManager = userManager;
            _orderService = orderService;
            _tenantService = tenantService; // [SA-3]
        }

        // ─────────────────────────────────────────────────────────────────────
        // GET /App/Orders
        // ─────────────────────────────────────────────────────────────────────
        // ── GET /App/Orders ───────────────────────────────────────────────────────
        public async Task<IActionResult> Index(string tab = "active", string? searchTable = null)
        {
            ViewData["Title"] = "Siparişler";
            ViewData["ActiveOrderCount"] = await _db.Orders.CountAsync(o => o.OrderStatus == OrderStatus.Open);
            ViewData["ActiveTab"] = tab;
            ViewBag.SearchTable = searchTable;

            var localNow = DateTime.Now;
            var todayLocalStart = new DateTime(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Local);
            var todayUtcStart = todayLocalStart.ToUniversalTime();

            // [PERF] AsNoTracking: listeleme sayfası salt okunur, change tracker'a gerek yok.
            // [PERF] AsSplitQuery: OrderItems (1:N) + MenuItem ThenInclude → 2 collection
            //        AsSplitQuery olmadan EF Core tek JOIN üretir → satır sayısı N×M'e çıkar.
            var allActiveOrders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.OrderStatus == OrderStatus.Open)
                .Include(o => o.Table)
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                .AsSplitQuery()
                .OrderBy(o => o.OrderOpenedAt)
                .ToListAsync();

            // KPI hesabı için hafif projeksiyon — Include gereksiz, AsNoTracking yeterli
            var allTodayPastOrders = await _db.Orders
                .AsNoTracking()
                .Where(o => (o.OrderStatus == OrderStatus.Paid || o.OrderStatus == OrderStatus.Cancelled)
                         && o.OrderOpenedAt >= todayUtcStart)
                .ToListAsync();

            ViewBag.AllActiveOrders = allActiveOrders;
            ViewBag.AllTodayRevenue = allTodayPastOrders
                .Where(o => o.OrderStatus == OrderStatus.Paid).Sum(o => o.OrderTotalAmount);
            ViewBag.AllTodayPaidCount = allTodayPastOrders.Count(o => o.OrderStatus == OrderStatus.Paid);

            // [PERF] AsNoTracking + AsSplitQuery:
            //        3 collection Include (Table, OrderItems→MenuItem, Payments)
            //        Kartezyen çarpım: 50 sipariş × 10 kalem × 5 ödeme = 2500 satır
            //        AsSplitQuery ile 3 ayrı SELECT → 50+500+250 = 800 satır
            var pastOrdersQuery = _db.Orders
                .AsNoTracking()
                .Where(o => (o.OrderStatus == OrderStatus.Paid || o.OrderStatus == OrderStatus.Cancelled)
                         && o.OrderOpenedAt >= todayUtcStart)
                .Include(o => o.Table)
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                .Include(o => o.Payments)
                .AsSplitQuery()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTable))
                pastOrdersQuery = pastOrdersQuery.Where(o =>
                    o.Table != null && o.Table.TableName.ToLower().Contains(searchTable.ToLower()));

            var pastOrders = await pastOrdersQuery.OrderByDescending(o => o.OrderClosedAt).ToListAsync();
            var activeOrders = allActiveOrders.ToList();

            if (!string.IsNullOrWhiteSpace(searchTable))
                activeOrders = activeOrders
                    .Where(o => o.Table != null &&
                        o.Table.TableName.Contains(searchTable, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            ViewBag.ActiveOrders = activeOrders;
            ViewBag.PastOrders = pastOrders;
            return View();
        }

        // ─────────────────────────────────────────────────────────────────────
        // GET /App/Orders/Create?tableId=5
        // ─────────────────────────────────────────────────────────────────────
        public async Task<IActionResult> Create(int tableId)
        {
            var table = await _db.Tables.FirstOrDefaultAsync(t => t.TableId == tableId);
            if (table == null)
            {
                TempData["Error"] = "Masa bulunamadı.";
                // [S5-URL] Cross-controller redirect → area zorunlu
                return RedirectToAction("Index", "Tables", new { area = "App" });
            }

            if (table.TableStatus == 1)
            {
                var existing = await _db.Orders
                    .FirstOrDefaultAsync(o => o.TableId == tableId && o.OrderStatus == OrderStatus.Open);
                if (existing != null)
                    // [S5-URL] Aynı controller, aynı area → area parametresi gerekmez
                    return RedirectToAction(nameof(Detail), new { id = existing.OrderId });
            }

            var categories = await _db.Categories
                .Where(c => c.IsActive).OrderBy(c => c.CategorySortOrder)
                .Include(c => c.MenuItems.Where(m =>
                    !m.IsDeleted && (m.IsAvailable || (m.TrackStock && m.StockQuantity <= 0))))
                .ToListAsync();

            ViewData["Title"] = $"{table.TableName} — Adisyon Aç";
            ViewBag.Table = table;
            ViewBag.Categories = categories;
            return View();
        }

        // ─────────────────────────────────────────────────────────────────────
        // POST /App/Orders/Create
        // ─────────────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] OrderCreateDto dto)
        {
            if (dto == null || dto.Items == null || !dto.Items.Any())
                return Json(new { success = false, message = "En az bir ürün eklemelisiniz." });

            // ── [SPRINT-1] Sipariş Alma Kilidi ──────────────────────────────
            // Aktif (açık) bir vardiya olmadan yeni sipariş açılamaz.
            var hasActiveShift = await _db.ShiftLogs.AnyAsync(s => !s.IsClosed);
            if (!hasActiveShift)
                return Json(new
                {
                    success = false,
                    message = "Aktif vardiya bulunamadı. Sipariş açabilmek için önce vardiyayı başlatın."
                });
            // ─────────────────────────────────────────────────────────────────

            var currentUser = await _userManager.GetUserAsync(User);
            var openedBy = currentUser?.FullName?.Trim();
            if (string.IsNullOrWhiteSpace(openedBy))
                openedBy = currentUser?.UserName ?? "Bilinmiyor";

            var result = await _orderService.CreateOrderAsync(dto, openedBy!);

            if (!result.Success)
                return Json(new { success = false, message = result.Message });

            return Json(new
            {
                success = true,
                message = result.Message,
                // [S5-URL] Cross-controller Url.Action → area zorunlu
                redirectUrl = Url.Action("Index", "Tables", new { area = "App" })
            });
        }

        // ── GET /App/Orders/Detail/{id} ───────────────────────────────────────────
        // [SA-3] İKİNCİ SAVUNMA HATTI — ID Manipulation Koruması
        // Global Query Filter adisyonu zaten mevcut tenant'a filtreler.
        // Ancak TenantId claim'i edge case'de null döndüğünde filtre devre dışı kalır.
        // Bu nedenle sorgudan sonra açık bir sahiplik kontrolü yapılır.
        public async Task<IActionResult> Detail(int id)
        {
            // [PERF] AsNoTracking: Detail sayfası salt okunur, entity takibine gerek yok.
            // [PERF] AsSplitQuery: OrderItems (1:N) + Payments (1:N) iki collection →
            //        tek JOIN'de kartezyen çarpım → AsSplitQuery ile 3 ayrı SELECT.
            var order = await _db.Orders
                .AsNoTracking()
                .Include(o => o.Table)
                .Include(o => o.OrderItems.OrderBy(i => i.OrderItemAddedAt)).ThenInclude(oi => oi.MenuItem)
                .Include(o => o.Payments)
                .AsSplitQuery()
                .FirstOrDefaultAsync(o => o.OrderId == id);

            // [SA-3] Çift kontrol:
            //   1. order == null  → GQF bu tenant'a ait olmayan kaydı gizledi.
            //   2. order.TenantId != _tenantService.TenantId → filter bypass edge case.
            if (order == null || order.TenantId != _tenantService.TenantId)
                return NotFound();

            // [PERF] AsNoTracking: kategoriler salt okunur, View'da değiştirilmiyor.
            var categories = await _db.Categories
                .AsNoTracking()
                .Where(c => c.IsActive).OrderBy(c => c.CategorySortOrder)
                .Include(c => c.MenuItems.Where(m => m.IsAvailable && !m.IsDeleted))
                .ToListAsync();

            ViewData["Title"] = $"{order.Table?.TableName} — Adisyon #{order.OrderId}";
            ViewBag.Categories = categories;
            return View(order);
        }

        // ─────────────────────────────────────────────────────────────────────
        // POST /App/Orders/UpdateItemStatus
        // ─────────────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateItemStatus([FromBody] OrderItemStatusUpdateDto dto)
        {
            var result = await _orderService.UpdateItemStatusAsync(dto);
            return Json(new { success = result.Success, message = result.Message });
        }

        // ─────────────────────────────────────────────────────────────────────
        // POST /App/Orders/AddItem
        // ─────────────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddItem([FromBody] OrderItemAddDto dto)
        {
            var result = await _orderService.AddItemAsync(dto);
            return Json(new { success = result.Success, message = result.Message });
        }

        // ─────────────────────────────────────────────────────────────────────
        // POST /App/Orders/AddItemBulk
        //  [PERF] N+1 çözümü → OrderService.AddItemBulkAsync() içinde
        //  tek WHERE IN sorgusu: MenuItemId listesi → Dictionary
        // ─────────────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddItemBulk([FromBody] BulkAddDto req)
        {
            if (req == null || req.Items == null || !req.Items.Any())
                return Json(new { success = false, message = "Eklenecek ürün bulunamadı." });
            
            var result = await _orderService.AddItemBulkAsync(req);
            return Json(new { success = result.Success, message = result.Message });
        }

        // ─────────────────────────────────────────────────────────────────────
        // POST /App/Orders/AddPayment
        // ─────────────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddPayment([FromBody] OrderPaymentDto dto)
        {
            var result = await _orderService.AddPaymentAsync(dto);
            if (!result.Success)
                return Json(new { success = false, message = result.Message });

            bool isClosed = result.Data?.IsClosed ?? false;
            return Json(new
            {
                success = true,
                message = result.Message,
                // [S5-URL] Adisyon kapandıysa Orders listesine yönlendir
                redirectUrl = isClosed
                    ? Url.Action("Index", "Orders", new { area = "App" })
                    : (string?)null
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // POST /App/Orders/Close  (tek seferlik tam ödeme)
        // ─────────────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Close([FromBody] OrderCloseDto dto)
        {
            var result = await _orderService.CloseAsync(dto);
            if (!result.Success)
                return Json(new { success = false, message = result.Message });

            return Json(new
            {
                success = true,
                message = result.Message,
                // [S5-URL] Cross-controller → area zorunlu
                redirectUrl = Url.Action("Index", "Tables", new { area = "App" })
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // POST /App/Orders/CloseZero
        // ─────────────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CloseZero([FromBody] OrderCloseZeroDto dto)
        {
            var result = await _orderService.CloseZeroAsync(dto);
            if (!result.Success)
                return Json(new { success = false, message = result.Message });

            return Json(new
            {
                success = true,
                message = result.Message,
                // [S5-URL] Cross-controller → area zorunlu
                redirectUrl = Url.Action("Index", "Tables", new { area = "App" })
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // POST /App/Orders/CancelItem
        // ─────────────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelItem([FromBody] OrderItemCancelDto dto)
        {
            var result = await _orderService.CancelItemAsync(dto);
            if (!result.Success)
                return Json(new { success = false, message = result.Message });

            bool autoClose = result.Data?.OrderAutoClose ?? false;
            return Json(new
            {
                success = true,
                message = result.Message,
                // [S5-URL] Cross-controller → area zorunlu
                redirectUrl = autoClose
                    ? Url.Action("Index", "Tables", new { area = "App" })
                    : (string?)null
            });
        }
    }
}
// ════════════════════════════════════════════════════════════════════════════
//  Controllers/OrdersController.cs  —  FAZ 1 FİNAL: Thin Controller
//
//  REFACTOR ÖZETI:
//  ──────────────
//  ÖNCE: 700+ satır, tüm iş mantığı burada, string karşılaştırmalar, N+1 sorgu
//  SONRA: ~180 satır, sadece HTTP → Service → JSON köprüsü
//
//  NE KALDI?
//  ─────────
//  - GET Index  : Okuma sorgusu, iş mantığı yok → controller'da kalır
//  - GET Create : Okuma sorgusu, iş mantığı yok → controller'da kalır
//  - GET Detail : Okuma sorgusu, iş mantığı yok → controller'da kalır
//
//  NE GİTTİ?
//  ─────────
//  - Tüm POST action içleri → OrderService'e taşındı
//  - SignalR inject → OrderService üstlendi
//  - ITenantService inject → OrderService üstlendi (servis tenant-aware)
//  - Transaction yönetimi → OrderService üstlendi
//  - Stok kontrol mantığı → OrderService üstlendi
//
//  ENUM GEÇİŞİ:
//  ─────────────
//  GET sorgularda artık OrderStatus.Open, OrderStatus.Paid gibi enum
//  kullanılıyor. Value Converter DB'de hâlâ "open"/"paid" saklıyor.
//
//  MODÜLER MONOLİTH GEÇİŞ YOLU:
//  ──────────────────────────────
//  İleride OrderService → ayrı mikro servis olduğunda bu controller
//  sadece HTTP client'a çevrilir; action imzaları değişmez.
// ════════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Dtos.Orders;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Modules.Orders;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared.Common;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Controllers
{
    [Authorize(Roles = "Admin,Garson,Kasiyer")]
    public class OrdersController : Controller
    {
        // Okuma sorguları için DbContext; yazma işlemleri OrderService üstlenir
        private readonly RestaurantDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IOrderService _orderService;

        public OrdersController(
            RestaurantDbContext db,
            UserManager<ApplicationUser> userManager,
            IOrderService orderService)
        {
            _db = db;
            _userManager = userManager;
            _orderService = orderService;
        }

        // ─────────────────────────────────────────────────────────────
        // GET /Orders
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> Index(string tab = "active", string? searchTable = null)
        {
            ViewData["Title"] = "Siparişler";
            ViewData["ActiveOrderCount"] = await _db.Orders.CountAsync(o => o.OrderStatus == OrderStatus.Open);
            ViewData["ActiveTab"] = tab;
            ViewBag.SearchTable = searchTable;

            var localNow = DateTime.Now;
            var todayLocalStart = new DateTime(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Local);
            var todayUtcStart = todayLocalStart.ToUniversalTime();

            var allActiveOrders = await _db.Orders
                .Where(o => o.OrderStatus == OrderStatus.Open)
                .Include(o => o.Table)
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                .OrderBy(o => o.OrderOpenedAt)
                .ToListAsync();

            var allTodayPastOrders = await _db.Orders
                .Where(o => (o.OrderStatus == OrderStatus.Paid || o.OrderStatus == OrderStatus.Cancelled)
                         && o.OrderOpenedAt >= todayUtcStart)
                .ToListAsync();

            ViewBag.AllActiveOrders = allActiveOrders;
            ViewBag.AllTodayRevenue = allTodayPastOrders
                .Where(o => o.OrderStatus == OrderStatus.Paid).Sum(o => o.OrderTotalAmount);
            ViewBag.AllTodayPaidCount = allTodayPastOrders.Count(o => o.OrderStatus == OrderStatus.Paid);

            var pastOrdersQuery = _db.Orders
                .Where(o => (o.OrderStatus == OrderStatus.Paid || o.OrderStatus == OrderStatus.Cancelled)
                         && o.OrderOpenedAt >= todayUtcStart)
                .Include(o => o.Table)
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                .Include(o => o.Payments)
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

        // ─────────────────────────────────────────────────────────────
        // GET /Orders/Create?tableId=5
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> Create(int tableId)
        {
            var table = await _db.Tables.FindAsync(tableId);
            if (table == null) { TempData["Error"] = "Masa bulunamadı."; return RedirectToAction("Index", "Tables"); }

            if (table.TableStatus == 1)
            {
                var existing = await _db.Orders
                    .FirstOrDefaultAsync(o => o.TableId == tableId && o.OrderStatus == OrderStatus.Open);
                if (existing != null) return RedirectToAction(nameof(Detail), new { id = existing.OrderId });
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

        // ─────────────────────────────────────────────────────────────
        // POST /Orders/Create
        // ─────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] OrderCreateDto dto)
        {
            if (dto == null || dto.Items == null || !dto.Items.Any())
                return Json(new { success = false, message = "En az bir ürün eklemelisiniz." });

            // ── [SPRINT-1] Sipariş Alma Kilidi ───────────────────────────────
            // Aktif (açık) bir vardiya olmadan yeni sipariş açılamaz.
            // Patron dükkanı açmadan kimse kasaya para atamaz.
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
                redirectUrl = Url.Action("Index", "Tables")
            });
        }

        // ─────────────────────────────────────────────────────────────
        // GET /Orders/Detail/42
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> Detail(int id)
        {
            var order = await _db.Orders
                .Include(o => o.Table)
                .Include(o => o.OrderItems.OrderBy(i => i.OrderItemAddedAt)).ThenInclude(oi => oi.MenuItem)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order == null) { TempData["Error"] = "Adisyon bulunamadı."; return RedirectToAction(nameof(Index)); }

            var categories = await _db.Categories
                .Where(c => c.IsActive).OrderBy(c => c.CategorySortOrder)
                .Include(c => c.MenuItems.Where(m => m.IsAvailable && !m.IsDeleted))
                .ToListAsync();

            ViewData["Title"] = $"{order.Table?.TableName} — Adisyon #{order.OrderId}";
            ViewBag.Categories = categories;
            return View(order);
        }

        // ─────────────────────────────────────────────────────────────
        // POST /Orders/UpdateItemStatus
        // ─────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateItemStatus([FromBody] OrderItemStatusUpdateDto dto)
        {
            var result = await _orderService.UpdateItemStatusAsync(dto);
            return Json(new { success = result.Success, message = result.Message });
        }

        // ─────────────────────────────────────────────────────────────
        // POST /Orders/AddItem
        // ─────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddItem([FromBody] OrderItemAddDto dto)
        {
            var result = await _orderService.AddItemAsync(dto);
            return Json(new { success = result.Success, message = result.Message });
        }

        // ─────────────────────────────────────────────────────────────
        // POST /Orders/AddItemBulk
        //  [PERF] N+1 çözümü → OrderService.AddItemBulkAsync() içinde
        //  tek WHERE IN sorgusu: MenuItemId listesi → Dictionary
        // ─────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddItemBulk([FromBody] BulkAddDto req)
        {
            if (req == null || req.Items == null || !req.Items.Any())
                return Json(new { success = false, message = "Eklenecek ürün bulunamadı." });

            var result = await _orderService.AddItemBulkAsync(req);
            return Json(new { success = result.Success, message = result.Message });
        }

        // ─────────────────────────────────────────────────────────────
        // POST /Orders/AddPayment
        // ─────────────────────────────────────────────────────────────
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
                redirectUrl = isClosed ? Url.Action("Index", "Orders") : (string?)null
            });
        }

        // ─────────────────────────────────────────────────────────────
        // POST /Orders/Close  (tek seferlik tam ödeme)
        // ─────────────────────────────────────────────────────────────
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
                redirectUrl = Url.Action("Index", "Tables")
            });
        }

        // ─────────────────────────────────────────────────────────────
        // POST /Orders/CloseZero
        // ─────────────────────────────────────────────────────────────
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
                redirectUrl = Url.Action("Index", "Tables")
            });
        }

        // ─────────────────────────────────────────────────────────────
        // POST /Orders/CancelItem
        // ─────────────────────────────────────────────────────────────
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
                redirectUrl = autoClose ? Url.Action("Index", "Tables") : (string?)null
            });
        }
    }
}
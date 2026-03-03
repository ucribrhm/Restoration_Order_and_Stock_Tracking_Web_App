// ════════════════════════════════════════════════════════════════════════════
//  Controllers/OrdersController.cs  —  SignalR Entegrasyonlu Versiyon
//  Yol: Restaurant_Order_and_Stock_Tracking_Web_App.MVC/Controllers/
//
//  DEĞİŞİKLİKLER (mevcut koda göre):
//  1. using Microsoft.AspNetCore.SignalR      →  eklendi
//  2. using ...Hubs                           →  eklendi
//  3. _hub (IHubContext<NotificationHub>)     →  field + constructor inject
//  4. NotifyAsync() private helper            →  eklendi
//  5. POST Create     → CommitAsync sonrası  🧾 "Adisyon Açıldı" bildirimi
//  6. POST AddItem    → CommitAsync sonrası  🍽️ "Ürün Eklendi"  bildirimi
//  7. POST AddItemBulk→ CommitAsync sonrası  🛒 "Sipariş Güncellendi"
//  8. POST AddPayment → isClosed=true sonrası ✅ "Adisyon Kapatıldı"
//  9. POST Close      → CommitAsync sonrası  ✅ "Hesap Kapatıldı"
// 10. POST CloseZero  → CommitAsync sonrası  🚫 "İptal Edildi"
//
//  Orijinal iş mantığı, validasyon ve hata yönetimi DOKUNULMADAN korunmuştur.
// ════════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Dtos.Orders;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Controllers
{
    [Authorize(Roles = "Admin,Garson,Kasiyer")]
    public class OrdersController : Controller
    {
        private readonly RestaurantDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<NotificationHub> _hub;          // Dashboard bildirimleri
        private readonly IHubContext<RestaurantHub> _restaurantHub; // KDS + genel broadcast
        private readonly ITenantService _tenantService;              // [SIG] Tenant izolasyonu

        public OrdersController(
            RestaurantDbContext db,
            UserManager<ApplicationUser> userManager,
            IHubContext<NotificationHub> hub,
            IHubContext<RestaurantHub> restaurantHub,
            ITenantService tenantService)
        {
            _db = db;
            _userManager = userManager;
            _hub = hub;
            _restaurantHub = restaurantHub;
            _tenantService = tenantService;
        }

        // ═════════════════════════════════════════════════════════════
        //  ÖZEL YARDIMCI: Dashboard'a SignalR bildirimi gönder.
        //  Fire-and-forget; hub hatası controller'ı durdurmamalı.
        //  Renk kodu:  turuncu=#f97316 | yeşil=#22c55e | kırmızı=#ef4444
        // ═════════════════════════════════════════════════════════════
        private Task NotifyAsync(string icon, string message, string color = "#f97316")
            => _hub.Clients.Group(_tenantService.TenantId ?? "").SendAsync("ReceiveNotification", new // [SIG] Clients.All→Group
            {
                icon,
                message,
                color,
                time = DateTime.Now.ToString("HH:mm")
            });

        // ─────────────────────────────────────────────────────────────
        // GET /Orders
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> Index(string tab = "active", string? searchTable = null)
        {
            ViewData["Title"] = "Siparişler";
            ViewData["ActiveOrderCount"] = await _db.Orders.CountAsync(o => o.OrderStatus == "open");
            ViewData["ActiveTab"] = tab;
            ViewBag.SearchTable = searchTable;

            var localNow = DateTime.Now;
            var todayLocalStart = new DateTime(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, DateTimeKind.Local);
            var todayUtcStart = todayLocalStart.ToUniversalTime();

            var allActiveOrders = await _db.Orders
                .Where(o => o.OrderStatus == "open")
                .Include(o => o.Table)
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                .OrderBy(o => o.OrderOpenedAt)
                .ToListAsync();

            var allTodayPastOrders = await _db.Orders
                .Where(o => (o.OrderStatus == "paid" || o.OrderStatus == "cancelled")
                         && o.OrderOpenedAt >= todayUtcStart)
                .ToListAsync();

            ViewBag.AllActiveOrders = allActiveOrders;
            ViewBag.AllTodayRevenue = allTodayPastOrders.Where(o => o.OrderStatus == "paid").Sum(o => o.OrderTotalAmount);
            ViewBag.AllTodayPaidCount = allTodayPastOrders.Count(o => o.OrderStatus == "paid");

            var activeOrders = allActiveOrders.ToList();
            var pastOrdersQuery = _db.Orders
                .Where(o => (o.OrderStatus == "paid" || o.OrderStatus == "cancelled")
                         && o.OrderOpenedAt >= todayUtcStart)
                .Include(o => o.Table)
                .Include(o => o.OrderItems).ThenInclude(oi => oi.MenuItem)
                .Include(o => o.Payments)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTable))
                pastOrdersQuery = pastOrdersQuery.Where(o => o.Table != null &&
                    o.Table.TableName.ToLower().Contains(searchTable.ToLower()));

            var pastOrders = await pastOrdersQuery.OrderByDescending(o => o.OrderClosedAt).ToListAsync();

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
                var existing = await _db.Orders.FirstOrDefaultAsync(o => o.TableId == tableId && o.OrderStatus == "open");
                if (existing != null) return RedirectToAction(nameof(Detail), new { id = existing.OrderId });
            }

            var categories = await _db.Categories
                .Where(c => c.IsActive).OrderBy(c => c.CategorySortOrder)
                .Include(c => c.MenuItems.Where(m =>
                    !m.IsDeleted &&
                    (m.IsAvailable || (m.TrackStock == true && m.StockQuantity <= 0))
                ))
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

            var currentUser = await _userManager.GetUserAsync(User);
            var openedBy = currentUser?.FullName?.Trim();
            if (string.IsNullOrWhiteSpace(openedBy))
                openedBy = currentUser?.UserName ?? "Bilinmiyor";

            var table = await _db.Tables.FindAsync(dto.TableId);
            if (table == null)
                return Json(new { success = false, message = "Masa bulunamadı." });

            foreach (var line in dto.Items)
            {
                if (line.Quantity < 1) continue;
                var miCheck = await _db.MenuItems.FindAsync(line.MenuItemId);
                if (miCheck == null) continue;
                if (miCheck.TrackStock == true && miCheck.StockQuantity < line.Quantity)
                    return Json(new
                    {
                        success = false,
                        stockQty = miCheck.StockQuantity,
                        message = $"Ürün stoğu = {miCheck.StockQuantity} kadar ekleme yapabilirsiniz. ({miCheck.MenuItemName})"
                    });
            }

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var order = new Order
                {
                    TableId = dto.TableId,
                    OrderStatus = "open",
                    OrderOpenedBy = openedBy.Trim(),
                    OrderNote = string.IsNullOrWhiteSpace(dto.OrderNote) ? null : dto.OrderNote.Trim(),
                    OrderTotalAmount = 0,
                    OrderOpenedAt = DateTime.UtcNow
                };
                _db.Orders.Add(order);
                await _db.SaveChangesAsync();

                decimal total = 0;
                foreach (var line in dto.Items)
                {
                    var mi = await _db.MenuItems.FindAsync(line.MenuItemId);
                    if (mi == null) continue;
                    int qty = line.Quantity < 1 ? 1 : line.Quantity;
                    decimal lineTotal = mi.MenuItemPrice * qty;

                    _db.OrderItems.Add(new OrderItem
                    {
                        OrderId = order.OrderId,
                        MenuItemId = mi.MenuItemId,
                        OrderItemQuantity = qty,
                        PaidQuantity = 0,
                        OrderItemUnitPrice = mi.MenuItemPrice,
                        OrderItemLineTotal = lineTotal,
                        OrderItemNote = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim(),
                        OrderItemStatus = "pending",
                        OrderItemAddedAt = DateTime.UtcNow
                    });
                    total += lineTotal;

                    if (mi.TrackStock)
                    {
                        mi.StockQuantity -= qty;
                        if (mi.StockQuantity <= 0) { mi.StockQuantity = 0; mi.IsAvailable = false; }
                    }
                }

                order.OrderTotalAmount = total;
                table.TableStatus = 1;
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                // ── ▼ YENİ SATIR: Dashboard'a gerçek zamanlı bildirim ──────
                _ = NotifyAsync("🧾",
                    $"{table.TableName} için yeni adisyon açıldı — ₺{total:N0}",
                    "#f97316");
                // ── ▲ YENİ ──────────────────────────────────────────────────

                return Json(new
                {
                    success = true,
                    message = "Adisyon açıldı.",
                    redirectUrl = Url.Action("Index", "Tables")
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Json(new { success = false, message = "Adisyon açılırken hata oluştu: " + ex.Message });
            }
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
            var validStatuses = new[] { "pending", "preparing", "served", "cancelled" };
            if (!validStatuses.Contains(dto.NewStatus))
                return Json(new { success = false, message = "Geçersiz durum." });

            var item = await _db.OrderItems.FindAsync(dto.OrderItemId);
            if (item == null)
                return Json(new { success = false, message = "Kalem bulunamadı." });

            item.OrderItemStatus = dto.NewStatus;
            await _db.SaveChangesAsync();

            return Json(new { success = true, message = "Durum güncellendi." });
        }

        // ─────────────────────────────────────────────────────────────
        // POST /Orders/AddItem
        // ─────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddItem([FromBody] OrderItemAddDto dto)
        {
            var order = await _db.Orders
                .Include(o => o.OrderItems)
                .Include(o => o.Table)
                .FirstOrDefaultAsync(o => o.OrderId == dto.OrderId);
            var mi = await _db.MenuItems.FindAsync(dto.MenuItemId);

            if (order == null || mi == null)
                return Json(new { success = false, message = "Adisyon veya ürün bulunamadı." });
            if (order.OrderStatus != "open")
                return Json(new { success = false, message = "Kapalı adisyona ürün eklenemez." });

            if (dto.Quantity < 1) dto.Quantity = 1;

            if (mi.TrackStock && mi.StockQuantity < dto.Quantity)
                return Json(new
                {
                    success = false,
                    stockQty = mi.StockQuantity,
                    message = $"Ürün stoğu = {mi.StockQuantity} kadar ekleme yapabilirsiniz."
                });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var noteNorm = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();

                var existing = order.OrderItems.FirstOrDefault(oi =>
                    oi.MenuItemId == dto.MenuItemId &&
                    oi.OrderItemStatus != "cancelled" &&
                    oi.PaidQuantity < oi.OrderItemQuantity &&
                    oi.OrderItemNote == noteNorm);

                if (existing != null)
                {
                    existing.OrderItemQuantity += dto.Quantity;
                    existing.OrderItemLineTotal = existing.OrderItemUnitPrice * existing.OrderItemQuantity;
                }
                else
                {
                    _db.OrderItems.Add(new OrderItem
                    {
                        OrderId = dto.OrderId,
                        MenuItemId = dto.MenuItemId,
                        OrderItemQuantity = dto.Quantity,
                        PaidQuantity = 0,
                        OrderItemUnitPrice = mi.MenuItemPrice,
                        OrderItemLineTotal = mi.MenuItemPrice * dto.Quantity,
                        OrderItemNote = noteNorm,
                        OrderItemStatus = "pending",
                        OrderItemAddedAt = DateTime.UtcNow
                    });
                }

                order.OrderTotalAmount += mi.MenuItemPrice * dto.Quantity;

                if (mi.TrackStock)
                {
                    mi.StockQuantity -= dto.Quantity;
                    if (mi.StockQuantity <= 0) { mi.StockQuantity = 0; mi.IsAvailable = false; }
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                // ── ▼ YENİ: Ürün ekleme bildirimi ──────────────────────────
                _ = NotifyAsync("🍽️",
                    $"{order.Table?.TableName ?? $"Adisyon #{order.OrderId}"} — {mi.MenuItemName} × {dto.Quantity} eklendi",
                    "#f97316");

                // ── KDS: Mutfak ekranına yeni sipariş kalemi bildir ──────────
                var newItem2 = await _db.OrderItems
                    .OrderByDescending(x => x.OrderItemId)
                    .FirstOrDefaultAsync(x => x.OrderId == dto.OrderId && x.MenuItemId == dto.MenuItemId);
                if (newItem2 != null)
                {
                    _ = _restaurantHub.Clients.Group(_tenantService.TenantId ?? "").SendAsync("NewOrderItem", new
                    {
                        orderItemId = newItem2.OrderItemId,
                        orderId = order.OrderId,
                        tableName = order.Table?.TableName ?? $"Adisyon #{order.OrderId}",
                        menuItemName = mi.MenuItemName,
                        quantity = newItem2.OrderItemQuantity,
                        note = newItem2.OrderItemNote,
                        addedAt = newItem2.OrderItemAddedAt
                    });
                }
                // ── ▲ YENİ ──────────────────────────────────────────────────

                return Json(new { success = true, message = $"{mi.MenuItemName} eklendi." });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Json(new { success = false, message = "Ürün eklenirken hata oluştu: " + ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // POST /Orders/AddItemBulk
        // ─────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddItemBulk([FromBody] BulkAddDto req)
        {
            if (req == null || req.Items == null || !req.Items.Any())
                return Json(new { success = false, message = "Eklenecek ürün bulunamadı." });

            var order = await _db.Orders
                .Include(o => o.OrderItems)
                .Include(o => o.Table)
                .FirstOrDefaultAsync(o => o.OrderId == req.OrderId);

            if (order == null)
                return Json(new { success = false, message = "Adisyon bulunamadı." });
            if (order.OrderStatus != "open")
                return Json(new { success = false, message = "Kapalı adisyona ürün eklenemez." });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                decimal totalAdded = 0;

                foreach (var line in req.Items)
                {
                    if (line.Quantity < 1) line.Quantity = 1;

                    var mi = await _db.MenuItems.FindAsync(line.MenuItemId);
                    if (mi == null) continue;

                    if (mi.TrackStock && mi.StockQuantity < line.Quantity)
                    {
                        await tx.RollbackAsync();
                        return Json(new
                        {
                            success = false,
                            stockQty = mi.StockQuantity,
                            message = $"Ürün stoğu = {mi.StockQuantity} kadar ekleme yapabilirsiniz. ({mi.MenuItemName})"
                        });
                    }

                    var noteNorm = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim();

                    var existing = order.OrderItems.FirstOrDefault(oi =>
                        oi.MenuItemId == line.MenuItemId &&
                        oi.OrderItemStatus != "cancelled" &&
                        oi.CancelledQuantity == 0 &&
                        oi.PaidQuantity < oi.OrderItemQuantity &&
                        oi.OrderItemNote == noteNorm);

                    if (existing != null)
                    {
                        existing.OrderItemQuantity += line.Quantity;
                        existing.OrderItemLineTotal = existing.OrderItemUnitPrice * existing.OrderItemQuantity;
                    }
                    else
                    {
                        _db.OrderItems.Add(new OrderItem
                        {
                            OrderId = req.OrderId,
                            MenuItemId = line.MenuItemId,
                            OrderItemQuantity = line.Quantity,
                            PaidQuantity = 0,
                            CancelledQuantity = 0,
                            OrderItemUnitPrice = mi.MenuItemPrice,
                            OrderItemLineTotal = mi.MenuItemPrice * line.Quantity,
                            OrderItemNote = noteNorm,
                            OrderItemStatus = "pending",
                            OrderItemAddedAt = DateTime.UtcNow
                        });
                    }

                    totalAdded += mi.MenuItemPrice * line.Quantity;

                    if (mi.TrackStock)
                    {
                        mi.StockQuantity -= line.Quantity;
                        if (mi.StockQuantity <= 0) { mi.StockQuantity = 0; mi.IsAvailable = false; }
                    }
                }

                order.OrderTotalAmount += totalAdded;
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                // ── ▼ YENİ: Toplu ekleme bildirimi ─────────────────────────
                _ = NotifyAsync("🛒",
                    $"{order.Table?.TableName ?? $"Adisyon #{order.OrderId}"} — {req.Items.Count} ürün eklendi (+₺{totalAdded:N0})",
                    "#f97316");

                // ── KDS: Her eklenen kalem için mutfak ekranını bildir ───────
                var addedItemIds = await _db.OrderItems
                    .Where(x => x.OrderId == req.OrderId && x.OrderItemStatus == "pending")
                    .OrderByDescending(x => x.OrderItemId)
                    .Take(req.Items.Count)
                    .ToListAsync();
                foreach (var kdsItem in addedItemIds)
                {
                    var kdsMenu = await _db.MenuItems.FindAsync(kdsItem.MenuItemId);
                    _ = _restaurantHub.Clients.Group(_tenantService.TenantId ?? "").SendAsync("NewOrderItem", new
                    {
                        orderItemId = kdsItem.OrderItemId,
                        orderId = order.OrderId,
                        tableName = order.Table?.TableName ?? $"Adisyon #{order.OrderId}",
                        menuItemName = kdsMenu?.MenuItemName ?? "Bilinmiyor",
                        quantity = kdsItem.OrderItemQuantity,
                        note = kdsItem.OrderItemNote,
                        addedAt = kdsItem.OrderItemAddedAt
                    });
                }
                // ── ▲ YENİ ──────────────────────────────────────────────────

                return Json(new { success = true, message = $"{req.Items.Count} ürün eklendi." });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Json(new { success = false, message = "Ürünler eklenirken hata oluştu: " + ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // POST /Orders/AddPayment
        // ─────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddPayment([FromBody] OrderPaymentDto dto)
        {
            if (dto.PaymentAmount <= 0)
                return Json(new { success = false, message = "Geçerli bir ödeme tutarı giriniz." });
            if (dto.DiscountValue < 0)
                return Json(new { success = false, message = "İndirim değeri negatif olamaz." });
            if (dto.DiscountType == "percent" && dto.DiscountValue > 100)
                return Json(new { success = false, message = "Yüzde indirim 0–100 arasında olmalıdır." });

            var order = await _db.Orders
                .Include(o => o.Payments)
                .Include(o => o.Table)
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.OrderId == dto.OrderId);

            if (order == null)
                return Json(new { success = false, message = "Adisyon bulunamadı." });
            if (order.OrderStatus != "open")
                return Json(new { success = false, message = "Bu adisyon zaten kapatılmış." });

            decimal discountAmount = dto.DiscountType == "percent"
                ? Math.Round(order.OrderTotalAmount * (dto.DiscountValue / 100m), 2)
                : Math.Round(dto.DiscountValue, 2);

            discountAmount = Math.Min(Math.Max(discountAmount, 0), order.OrderTotalAmount);

            var netTotal = order.OrderTotalAmount - discountAmount;
            var alreadyPaid = order.Payments.Sum(p => p.PaymentsAmount);
            var remaining = netTotal - alreadyPaid;

            if (dto.PaymentAmount > remaining + 0.01m)
                return Json(new { success = false, message = $"Ödeme tutarı kalan tutarı (₺{remaining:N2}) aşamaz." });

            int methodCode = dto.PaymentMethod switch { "credit_card" => 1, "debit_card" => 2, "other" => 3, _ => 0 };

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                _db.Payments.Add(new Payment
                {
                    OrderId = dto.OrderId,
                    PaymentsMethod = methodCode,
                    PaymentsAmount = dto.PaymentAmount,
                    PaymentsChangeGiven = 0,
                    PaymentsPaidAt = DateTime.UtcNow,
                    PaymentsNote = string.IsNullOrWhiteSpace(dto.PayerName) ? "" : dto.PayerName.Trim()
                });

                bool hasItemSel = dto.PaidItems != null && dto.PaidItems.Any();

                if (hasItemSel)
                {
                    foreach (var sel in dto.PaidItems!)
                    {
                        var oi = order.OrderItems.FirstOrDefault(x => x.OrderItemId == sel.OrderItemId);
                        if (oi == null || sel.Quantity <= 0) continue;
                        int canPay = oi.OrderItemQuantity - oi.PaidQuantity;
                        oi.PaidQuantity += Math.Min(sel.Quantity, canPay);
                    }
                }
                else
                {
                    decimal budget = dto.PaymentAmount;
                    var unpaid = order.OrderItems
                        .Where(oi => oi.OrderItemStatus != "cancelled" && oi.PaidQuantity < oi.OrderItemQuantity)
                        .OrderBy(oi => oi.OrderItemAddedAt).ToList();

                    foreach (var oi in unpaid)
                    {
                        if (budget <= 0.001m) break;
                        int remaining2 = oi.OrderItemQuantity - oi.PaidQuantity;
                        int canAfford = (int)Math.Floor(budget / oi.OrderItemUnitPrice);
                        int payQty = Math.Min(canAfford, remaining2);
                        if (payQty > 0) { oi.PaidQuantity += payQty; budget -= payQty * oi.OrderItemUnitPrice; }
                    }
                }

                var newTotalPaid = alreadyPaid + dto.PaymentAmount;
                bool isClosed = newTotalPaid >= netTotal - 0.01m;

                if (isClosed)
                {
                    foreach (var oi in order.OrderItems.Where(x => x.OrderItemStatus != "cancelled"))
                        oi.PaidQuantity = oi.OrderItemQuantity;

                    order.OrderStatus = "paid";
                    order.OrderClosedAt = DateTime.UtcNow;
                    if (order.Table != null) order.Table.TableStatus = 0;
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                if (isClosed)
                {
                    // ── ▼ YENİ: Kısmi/tam ödemeyle adisyon kapandı ─────────
                    _ = NotifyAsync("✅",
                        $"{order.Table?.TableName ?? $"Adisyon #{order.OrderId}"} hesabını kapattı — ₺{netTotal:N0}",
                        "#22c55e");
                    // ── ▲ YENİ ──────────────────────────────────────────────

                    return Json(new { success = true, message = "Adisyon kapatıldı, ödeme tamamlandı.", redirectUrl = Url.Action("Index", "Orders") });
                }

                return Json(new
                {
                    success = true,
                    message = $"₺{dto.PaymentAmount:N2} ödeme alındı. Kalan: ₺{netTotal - newTotalPaid:N2}",
                    redirectUrl = (string?)null
                });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Json(new { success = false, message = "Ödeme kaydedilirken hata oluştu: " + ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // POST /Orders/Close  (tek seferlik tam ödeme)
        // ─────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Close([FromBody] OrderCloseDto dto)
        {
            if (dto.PaymentAmount <= 0)
                return Json(new { success = false, message = "Geçerli bir tutar giriniz." });

            var order = await _db.Orders
                .Include(o => o.Table)
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.OrderId == dto.OrderId);

            if (order == null)
                return Json(new { success = false, message = "Adisyon bulunamadı." });
            if (order.OrderStatus != "open")
                return Json(new { success = false, message = "Bu adisyon zaten kapatılmış." });
            if (dto.PaymentAmount < order.OrderTotalAmount)
                return Json(new { success = false, message = "Ödeme tutarı toplam tutardan az olamaz." });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                _db.Payments.Add(new Payment
                {
                    OrderId = dto.OrderId,
                    PaymentsMethod = dto.PaymentMethod == "card" ? 1 : 0,
                    PaymentsAmount = dto.PaymentAmount,
                    PaymentsChangeGiven = dto.PaymentAmount - order.OrderTotalAmount,
                    PaymentsPaidAt = DateTime.UtcNow,
                    PaymentsNote = ""
                });

                foreach (var oi in order.OrderItems.Where(x => x.OrderItemStatus != "cancelled"))
                    oi.PaidQuantity = oi.OrderItemQuantity;

                order.OrderStatus = "paid";
                order.OrderClosedAt = DateTime.UtcNow;
                order.Table!.TableStatus = 0;
                var tableName = order.Table.TableName;
                var total = order.OrderTotalAmount;

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                // ── ▼ YENİ: Tam ödeme kapanma bildirimi ────────────────────
                _ = NotifyAsync("✅",
                    $"{tableName} hesabını kapattı — ₺{total:N0}",
                    "#22c55e");
                // ── ▲ YENİ ──────────────────────────────────────────────────

                return Json(new { success = true, message = "Adisyon kapatıldı, ödeme alındı.", redirectUrl = Url.Action("Index", "Tables") });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Json(new { success = false, message = "Adisyon kapatılırken hata oluştu: " + ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // POST /Orders/CloseZero
        // ─────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CloseZero([FromBody] OrderCloseZeroDto dto)
        {
            var order = await _db.Orders
                .Include(o => o.Table)
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.OrderId == dto.OrderId);

            if (order == null)
                return Json(new { success = false, message = "Adisyon bulunamadı." });
            if (order.OrderStatus != "open")
                return Json(new { success = false, message = "Bu adisyon zaten kapatılmış." });
            if (order.OrderTotalAmount > 0.001m)
                return Json(new { success = false, message = "Adisyon tutarı sıfır olmadığı için bu yöntemle kapatılamaz." });

            bool hasActiveItems = order.OrderItems.Any(oi =>
                oi.OrderItemStatus != "cancelled" &&
                (oi.OrderItemQuantity - oi.CancelledQuantity) > 0);

            if (hasActiveItems)
                return Json(new { success = false, message = "Adisyonda hâlâ aktif ürünler var. Önce tüm ürünleri iptal edin." });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var tableName = order.Table?.TableName ?? $"Adisyon #{order.OrderId}";

                order.OrderStatus = "cancelled";
                order.OrderClosedAt = DateTime.UtcNow;
                if (order.Table != null) order.Table.TableStatus = 0;

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                // ── ▼ YENİ: Sıfır-tutarlı adisyon iptal bildirimi ──────────
                _ = NotifyAsync("🚫",
                    $"{tableName} — sıfır tutarlı adisyon iptal edildi",
                    "#ef4444");
                // ── ▲ YENİ ──────────────────────────────────────────────────

                return Json(new { success = true, message = "Sıfır tutarlı adisyon kapatıldı, masa boşaltıldı.", redirectUrl = Url.Action("Index", "Tables") });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Json(new { success = false, message = "Adisyon kapatılırken bir hata oluştu: " + ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────
        // POST /Orders/CancelItem
        // ─────────────────────────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelItem([FromBody] OrderItemCancelDto dto)
        {
            if (dto.CancelQty < 1)
                return Json(new { success = false, message = "İptal miktarı en az 1 olmalıdır." });

            var item = await _db.OrderItems
                .Include(oi => oi.MenuItem)
                .FirstOrDefaultAsync(oi => oi.OrderItemId == dto.OrderItemId);

            var order = await _db.Orders.FindAsync(dto.OrderId);

            if (item == null || order == null)
                return Json(new { success = false, message = "Kalem veya adisyon bulunamadı." });
            if (order.OrderStatus != "open")
                return Json(new { success = false, message = "Kapalı adisyonda iptal yapılamaz." });

            int activeQty = item.OrderItemQuantity - item.CancelledQuantity;
            int cancelable = activeQty - item.PaidQuantity;

            if (dto.CancelQty > cancelable)
                return Json(new { success = false, message = $"En fazla {cancelable} adet iptal edilebilir ({item.PaidQuantity} adet zaten ödendi)." });

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                decimal refund = item.OrderItemUnitPrice * dto.CancelQty;

                item.CancelledQuantity += dto.CancelQty;
                item.CancelReason = string.IsNullOrWhiteSpace(dto.CancelReason) ? null : dto.CancelReason.Trim();
                item.OrderItemLineTotal = item.OrderItemUnitPrice * (item.OrderItemQuantity - item.CancelledQuantity);

                if (item.OrderItemQuantity - item.CancelledQuantity <= 0)
                    item.OrderItemStatus = "cancelled";

                order.OrderTotalAmount = Math.Max(0, order.OrderTotalAmount - refund);

                bool tracksStock = item.MenuItem != null && item.MenuItem.TrackStock;

                if (tracksStock)
                {
                    item.IsWasted = dto.IsWasted ?? false;
                    int prevStock = item.MenuItem!.StockQuantity;

                    if (dto.IsWasted != true)
                    {
                        item.MenuItem!.StockQuantity += dto.CancelQty;
                        if (!item.MenuItem.IsAvailable && item.MenuItem.StockQuantity > 0)
                            item.MenuItem.IsAvailable = true;
                    }

                    _db.StockLogs.Add(new StockLog
                    {
                        MenuItemId = item.MenuItem!.MenuItemId,
                        MovementType = dto.IsWasted == true ? "Çıkış" : "Giriş",
                        QuantityChange = dto.IsWasted == true ? -dto.CancelQty : dto.CancelQty,
                        PreviousStock = prevStock,
                        NewStock = dto.IsWasted == true ? prevStock : prevStock + dto.CancelQty,
                        Note = dto.IsWasted == true
                            ? $"Zayi/Fire — Adisyon #{dto.OrderId}, {dto.CancelQty} adet"
                            : $"İptal iadesi — Adisyon #{dto.OrderId}, {dto.CancelQty} adet",
                        SourceType = dto.IsWasted == true ? "SiparişKaynaklı" : null,
                        OrderId = dto.IsWasted == true ? dto.OrderId : null,
                        UnitPrice = item.OrderItemUnitPrice,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    item.IsWasted = null;
                }

                await _db.SaveChangesAsync();

                var freshPayments = await _db.Payments
                    .Where(p => p.OrderId == order.OrderId)
                    .SumAsync(p => p.PaymentsAmount);

                if (order.OrderStatus == "open" && order.OrderTotalAmount - freshPayments <= 0.001m && freshPayments > 0)
                {
                    var tableForClose = await _db.Tables.FindAsync(order.TableId);

                    order.OrderStatus = "paid";
                    order.OrderClosedAt = DateTime.UtcNow;
                    if (tableForClose != null) tableForClose.TableStatus = 0;

                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();

                    string stockNote2 = tracksStock
                        ? (dto.IsWasted == true ? " | Zayi kaydedildi." : " | Stoka iade edildi.")
                        : string.Empty;

                    return Json(new
                    {
                        success = true,
                        message = $"{dto.CancelQty} adet iptal edildi.{stockNote2} Kalan tutar sıfırlandı, adisyon kapatıldı.",
                        redirectUrl = Url.Action("Index", "Tables")
                    });
                }

                await tx.CommitAsync();

                string stockNote = tracksStock
                    ? (dto.IsWasted == true ? " | Zayi/Fire olarak işaretlendi." : " | Stoka iade edildi.")
                    : string.Empty;

                return Json(new { success = true, message = $"{dto.CancelQty} adet iptal edildi.{stockNote}", redirectUrl = (string?)null });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return Json(new { success = false, message = "İptal işlemi sırasında bir hata oluştu: " + ex.Message });
            }
        }
    }
}
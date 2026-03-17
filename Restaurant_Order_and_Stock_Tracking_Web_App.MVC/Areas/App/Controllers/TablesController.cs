// ════════════════════════════════════════════════════════════════════════════
//  Areas/App/Controllers/TablesController.cs
//  Yol: Areas/App/Controllers/TablesController.cs
//
//  SPRINT 5 — Areas Refactoring:
//  [S5-NS]   Namespace → ...Areas.App.Controllers
//  [S5-BASE] Controller → AppBaseController ([Area("App")] + AppAuth miras alındı)
//  [S5-URL]  Tüm Url.Action çağrıları aynı controller içi olduğundan
//            area parametresi gerekmez; ancak okunabilirlik için
//            explicit area eklendi. ServeReadyItemsDto sınıfı aynı
//            namespace içinde korundu.
//
//  SPRINT B.2 — Fire-and-Forget Düzeltmesi:
//  [B2-5] ILogger<TablesController> inject edildi
//  [B2-6] MergeOrder   → tüm `_ = _hub…` satırları await + try-catch + LogError'a çevrildi
//  [B2-7] ServeReadyItems → aynı dönüşüm
// ════════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Dtos.Tables;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Filters;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared.Common;
using System.Text.RegularExpressions;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.App.Controllers
{
    [Area("App")]
    [Authorize(Roles = "Admin,Garson,Kasiyer")]
    public class TablesController : AppBaseController
    {
        private readonly RestaurantDbContext _db;
        private readonly IHubContext<RestaurantHub> _hub;
        private readonly ITenantService _tenantService;
        private readonly ILogger<TablesController> _logger;         // [B2-5]

        public TablesController(
            RestaurantDbContext db,
            IHubContext<RestaurantHub> hub,
            ITenantService tenantService,
            ILogger<TablesController> logger)                       // [B2-5]
        {
            _db = db;
            _hub = hub;
            _tenantService = tenantService;
            _logger = logger;                                        // [B2-5]
        }

        // ── GET /App/Tables ───────────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            await CleanupExpiredReservationsAsync();

            ViewData["Title"] = "Masalar";
            ViewData["OccupiedCount"] = await _db.Tables.CountAsync(t => t.TableStatus == 1);

            var tables = await _db.Tables
                .Include(t => t.Orders.Where(o => o.OrderStatus == OrderStatus.Open))
                    .ThenInclude(o => o.OrderItems)
                        .ThenInclude(oi => oi.MenuItem)
                .ToListAsync();

            tables = tables
                .OrderBy(t => NaturalSortKey(t.TableName).prefix)
                .ThenBy(t => NaturalSortKey(t.TableName).number)
                .ThenBy(t => NaturalSortKey(t.TableName).suffix)
                .ToList();

            return View(tables);
        }

        // ── Yardımcı: Masa adı için doğal sıralama anahtarı ──────────────────
        private static (string prefix, int number, string suffix) NaturalSortKey(string name)
        {
            var m = Regex.Match(name ?? "", @"^(.*?)(\d+)(.*)$");
            if (m.Success)
                return (m.Groups[1].Value.ToLower(), int.Parse(m.Groups[2].Value), m.Groups[3].Value.ToLower());
            return ((name ?? "").ToLower(), 0, "");
        }

        // ── Yardımcı: Süresi geçmiş rezervasyonları temizle ──────────────────
        private async Task CleanupExpiredReservationsAsync()
        {
            var cutoff = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(30));
            var expired = await _db.Tables
                .Where(t => t.TableStatus == 2
                         && t.ReservationTime.HasValue
                         && t.ReservationTime.Value <= cutoff)
                .ToListAsync();

            if (!expired.Any()) return;

            foreach (var t in expired)
            {
                t.TableStatus = 0;
                t.ReservationName = null;
                t.ReservationPhone = null;
                t.ReservationGuestCount = null;
                t.ReservationTime = null;
            }
            await _db.SaveChangesAsync();
        }

        // ── POST /App/Tables/Create ───────────────────────────────────────────
        [Authorize(Roles = "Admin")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] TableCreateDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TableName))
                return Json(new { success = false, message = "Masa adı boş olamaz." });

            if (dto.TableCapacity < 1 || dto.TableCapacity > 20)
                return Json(new { success = false, message = "Kapasite 1 ile 20 arasında olmalıdır." });

            if (await _db.Tables.AnyAsync(t => t.TableName == dto.TableName.Trim()))
                return Json(new { success = false, message = $"'{dto.TableName}' adında bir masa zaten var." });

            try
            {
                _db.Tables.Add(new Table
                {
                    TableName = dto.TableName.Trim(),
                    TableCapacity = dto.TableCapacity,
                    TableStatus = 0,
                    TableCreatedAt = DateTime.UtcNow,
                    TenantId = _tenantService.TenantId!
                });
                await _db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"'{dto.TableName.Trim()}' başarıyla eklendi.",
                    redirectUrl = Url.Action(nameof(Index), "Tables", new { area = "App" })
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Masa eklenirken hata oluştu: " + ex.Message });
            }
        }

        // ── POST /App/Tables/Reserve ──────────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Reserve([FromBody] TableReserveDto dto)
        {
            var table = await _db.Tables.FirstOrDefaultAsync(t => t.TableId == dto.TableId);
            if (table == null)
                return Json(new { success = false, message = "Masa bulunamadı." });
            if (table.TableStatus != 0)
                return Json(new { success = false, message = "Yalnızca boş masalar rezerve edilebilir." });
            if (string.IsNullOrWhiteSpace(dto.ReservationName))
                return Json(new { success = false, message = "İsim soyisim boş olamaz." });
            if (string.IsNullOrWhiteSpace(dto.ReservationPhone))
                return Json(new { success = false, message = "Telefon numarası boş olamaz." });
            if (dto.ReservationGuestCount < 1 || dto.ReservationGuestCount > table.TableCapacity)
                return Json(new { success = false, message = $"Kişi sayısı 1 ile {table.TableCapacity} arasında olmalıdır." });
            if (!TimeSpan.TryParse(dto.ReservationTime, out TimeSpan parsedTime))
                return Json(new { success = false, message = "Geçerli bir rezervasyon saati giriniz." });

            var localNow = DateTime.Now;
            var reservationLocal = localNow.Date.Add(parsedTime);
            if (reservationLocal < localNow.AddMinutes(-5))
                return Json(new { success = false, message = "Rezervasyon saati geçmiş bir saat olamaz." });

            try
            {
                table.TableStatus = 2;
                table.ReservationName = dto.ReservationName.Trim();
                table.ReservationPhone = dto.ReservationPhone.Trim();
                table.ReservationGuestCount = dto.ReservationGuestCount;
                table.ReservationTime = DateTime.SpecifyKind(reservationLocal, DateTimeKind.Local).ToUniversalTime();

                await _db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"'{table.TableName}' — {dto.ReservationName} adına rezerve edildi.",
                    redirectUrl = Url.Action(nameof(Index), "Tables", new { area = "App" })
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Rezervasyon kaydedilirken hata oluştu: " + ex.Message });
            }
        }

        // ── POST /App/Tables/CancelReserve ────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelReserve([FromBody] TableReserveDto dto)
        {
            var table = await _db.Tables.FirstOrDefaultAsync(t => t.TableId == dto.TableId);
            if (table == null)
                return Json(new { success = false, message = "Masa bulunamadı." });
            if (table.TableStatus != 2)
                return Json(new { success = false, message = "Bu masa zaten rezerve değil." });

            try
            {
                table.TableStatus = 0;
                table.ReservationName = null;
                table.ReservationPhone = null;
                table.ReservationGuestCount = null;
                table.ReservationTime = null;

                await _db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"'{table.TableName}' rezervasyonu iptal edildi.",
                    redirectUrl = Url.Action(nameof(Index), "Tables", new { area = "App" })
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Rezervasyon iptal edilirken hata oluştu: " + ex.Message });
            }
        }

        // ── POST /App/Tables/Delete ───────────────────────────────────────────
        [Authorize(Roles = "Admin")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete([FromBody] TableDeleteDto dto)
        {
            var table = await _db.Tables.FirstOrDefaultAsync(t => t.TableId == dto.TableId);
            if (table == null)
                return Json(new { success = false, message = "Masa bulunamadı." });
            if (table.TableStatus == 1)
                return Json(new { success = false, message = "Açık adisyonu olan masa silinemez." });

            try
            {
                _db.Tables.Remove(table);
                await _db.SaveChangesAsync();

                return Json(new
                {
                    success = true,
                    message = $"'{table.TableName}' silindi.",
                    redirectUrl = Url.Action(nameof(Index), "Tables", new { area = "App" })
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Masa silinirken hata oluştu: " + ex.Message });
            }
        }

        // ── POST /App/Tables/MergeOrder ───────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]

        public async Task<IActionResult> MergeOrder([FromBody] TableMergeOrderDto dto)
        {
            if (dto.SourceTableId == dto.TargetTableId)
                return Json(new { success = false, message = "Kaynak ve hedef masa aynı olamaz." });

            var sourceOrder = await _db.Orders
                .Include(o => o.OrderItems)
                .Include(o => o.Payments)
                .Include(o => o.Table)
                .FirstOrDefaultAsync(o => o.TableId == dto.SourceTableId && o.OrderStatus == OrderStatus.Open);

            if (sourceOrder == null)
                return Json(new { success = false, message = "Kaynak masada açık adisyon bulunamadı." });

            var targetTable = await _db.Tables.FirstOrDefaultAsync(t => t.TableId == dto.TargetTableId);
            if (targetTable == null)
                return Json(new { success = false, message = "Hedef masa bulunamadı." });

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var targetOrder = await _db.Orders
                    .Include(o => o.OrderItems)
                    .FirstOrDefaultAsync(o => o.TableId == dto.TargetTableId && o.OrderStatus == OrderStatus.Open);

                if (targetOrder == null)
                {
                    // Hedef masada adisyon yok → adisyonu taşı
                    var oldOrderId = sourceOrder.OrderId;
                    sourceOrder.TableId = dto.TargetTableId;
                    sourceOrder.Table.TableStatus = 0;
                    targetTable.TableStatus = 1;
                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    // [B2-6] Fire-and-forget → await + try-catch + LogError
                    var tg = _tenantService.TenantId ?? "";
                    try
                    {
                        await _hub.Clients.Group(tg).SendAsync("RemoveOrderCard", new { orderId = oldOrderId });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "[TablesController] SignalR 'RemoveOrderCard' gönderilemedi — " +
                            "TenantGroup: {TenantGroup}, OrderId: {OrderId}",
                            tg, oldOrderId);
                    }

                    try
                    {
                        await _hub.Clients.Group(tg).SendAsync("OrderUpdated", new { orderId = oldOrderId });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "[TablesController] SignalR 'OrderUpdated' gönderilemedi — " +
                            "TenantGroup: {TenantGroup}, OrderId: {OrderId}",
                            tg, oldOrderId);
                    }

                    return Json(new
                    {
                        success = true,
                        message = $"Adisyon '{targetTable.TableName}' masasına taşındı.",
                        redirectUrl = Url.Action(nameof(Index), "Tables", new { area = "App" })
                    });
                }

                // Hedef masada da adisyon var → birleştir
                foreach (var srcItem in sourceOrder.OrderItems)
                {
                    var existing = targetOrder.OrderItems
                        .FirstOrDefault(ti => ti.MenuItemId == srcItem.MenuItemId);

                    if (existing != null)
                    {
                        existing.OrderItemQuantity += srcItem.OrderItemQuantity;
                        existing.OrderItemLineTotal += srcItem.OrderItemLineTotal;
                        if (StatusPriority(srcItem.OrderItemStatus) < StatusPriority(existing.OrderItemStatus))
                            existing.OrderItemStatus = srcItem.OrderItemStatus;
                        _db.OrderItems.Remove(srcItem);
                    }
                    else
                    {
                        srcItem.OrderId = targetOrder.OrderId;
                    }
                }

                foreach (var payment in sourceOrder.Payments)
                    payment.OrderId = targetOrder.OrderId;

                await _db.SaveChangesAsync();

                targetOrder.OrderTotalAmount = await _db.OrderItems
                    .Where(oi => oi.OrderId == targetOrder.OrderId && oi.OrderItemStatus != OrderItemStatus.Cancelled)
                    .SumAsync(oi => oi.OrderItemLineTotal);

                sourceOrder.OrderStatus = OrderStatus.Cancelled;
                sourceOrder.OrderClosedAt = DateTime.UtcNow;
                sourceOrder.OrderTotalAmount = 0;
                sourceOrder.Table.TableStatus = 0;
                targetTable.TableStatus = 1;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                // [B2-6] Fire-and-forget → await + try-catch + LogError
                var tg2 = _tenantService.TenantId ?? "";
                try
                {
                    await _hub.Clients.Group(tg2).SendAsync("RemoveOrderCard", new { orderId = sourceOrder.OrderId });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[TablesController] SignalR 'RemoveOrderCard' gönderilemedi — " +
                        "TenantGroup: {TenantGroup}, OrderId: {OrderId}",
                        tg2, sourceOrder.OrderId);
                }

                try
                {
                    await _hub.Clients.Group(tg2).SendAsync("OrderUpdated", new { orderId = targetOrder.OrderId });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[TablesController] SignalR 'OrderUpdated' gönderilemedi — " +
                        "TenantGroup: {TenantGroup}, OrderId: {OrderId}",
                        tg2, targetOrder.OrderId);
                }

                return Json(new
                {
                    success = true,
                    message = $"'{sourceOrder.Table.TableName}' adisyonu '{targetTable.TableName}' masasına birleştirildi.",
                    redirectUrl = Url.Action(nameof(Index), "Tables", new { area = "App" })
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Birleştirme sırasında hata oluştu: " + ex.Message });
            }
        }

        // ── POST /App/Tables/ServeReadyItems ──────────────────────────────────
        // Tables ekranındaki "Servis Et" butonundan çağrılır.
        // O masanın açık adisyonundaki tüm Ready kalemleri Served yapar.
        // SignalR: OrderServed + (gerekirse) RemoveOrderCard fırlatılır.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ServeReadyItems([FromBody] ServeReadyItemsDto dto)
        {
            var order = await _db.Orders
                .Where(o => o.TableId == dto.TableId && o.OrderStatus == OrderStatus.Open)
                .Include(o => o.Table)
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync();

            if (order == null)
                return Json(new { success = false, message = "Açık adisyon bulunamadı." });

            var readyItems = order.OrderItems
                .Where(oi => oi.OrderItemStatus == OrderItemStatus.Ready)
                .ToList();

            if (!readyItems.Any())
                return Json(new { success = false, message = "Servis edilecek hazır ürün yok." });

            foreach (var item in readyItems)
                item.OrderItemStatus = OrderItemStatus.Served;

            await _db.SaveChangesAsync();

            var tg = _tenantService.TenantId ?? "";
            var tableName = order.Table?.TableName ?? $"#{order.TableId}";

            // [B2-7] Fire-and-forget → await + try-catch + LogError
            // Garson rozeti kaldır
            try
            {
                await _hub.Clients.Group(tg).SendAsync("OrderServed", new
                {
                    orderId = order.OrderId,
                    tableId = dto.TableId,
                    tableName
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[TablesController] SignalR 'OrderServed' gönderilemedi — " +
                    "TenantGroup: {TenantGroup}, OrderId: {OrderId}",
                    tg, order.OrderId);
            }

            // KDS: hâlâ Pending/Preparing kalem varsa kartı güncelle, yoksa kaldır
            bool hasKitchenItems = order.OrderItems.Any(oi =>
                oi.OrderItemStatus == OrderItemStatus.Pending ||
                oi.OrderItemStatus == OrderItemStatus.Preparing);

            try
            {
                if (hasKitchenItems)
                    await _hub.Clients.Group(tg).SendAsync("OrderUpdated", new { orderId = order.OrderId });
                else
                    await _hub.Clients.Group(tg).SendAsync("RemoveOrderCard", new { orderId = order.OrderId });
            }
            catch (Exception ex)
            {
                var eventName = hasKitchenItems ? "OrderUpdated" : "RemoveOrderCard";
                _logger.LogError(ex,
                    "[TablesController] SignalR '{EventName}' gönderilemedi — " +
                    "TenantGroup: {TenantGroup}, OrderId: {OrderId}",
                    eventName, tg, order.OrderId);
            }

            return Json(new { success = true, message = $"{readyItems.Count} ürün servis edildi." });
        }

        // ── POST /App/Tables/DismissWaiter ────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DismissWaiter([FromBody] DismissWaiterDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TableName))
                return Json(new { success = false, message = "Geçersiz masa adı." });

            var table = await _db.Tables.FirstOrDefaultAsync(t => t.TableName == dto.TableName);
            if (table == null)
                return Json(new { success = false, message = "Masa bulunamadı." });

            table.IsWaiterCalled = false;
            table.WaiterCalledAt = null;
            await _db.SaveChangesAsync();

            // DismissWaiter zaten await kullanıyordu — değişiklik yok
            await _hub.Clients
                .Group(_tenantService.TenantId ?? "")
                .SendAsync("WaiterDismissed", new { tableName = table.TableName });
            Console.WriteLine($"🚨 [DISMISS TEST] İlgilenildi mesajı '{_tenantService.TenantId}' odasına atılıyor. Masa: {table.TableName}");
            return Json(new { success = true });
        }

        // ── Yardımcı: OrderItemStatus öncelik sırası ──────────────────────────
        private static int StatusPriority(OrderItemStatus status) => status switch
        {
            OrderItemStatus.Pending => 0,
            OrderItemStatus.Preparing => 1,
            OrderItemStatus.Served => 2,
            _ => 3
        };
    }

    // ── DTO: ServeReadyItems için basit request nesnesi ───────────────────────
    public class ServeReadyItemsDto
    {
        public int TableId { get; set; }
    }
}
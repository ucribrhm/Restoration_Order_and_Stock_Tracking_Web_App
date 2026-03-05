// ============================================================================
//  Controllers/KitchenController.cs  —  KDS v2
//  DEĞİŞİKLİK — FAZ 1 ADIM 3: SignalR Tenant İzolasyonu
//
//  [SIG] KitchenController [AllowAnonymous] olduğundan ITenantService null döner.
//  Çözüm: item.Order.TenantId değeri doğrudan DB nesnesinden okunur.
//  Bu değer Clients.Group(tenantId) için kullanılır.
//
//  DEĞİŞEN SATIRLAR:
//  1. UpdateStatus → item.Order.TenantId ile tenantId değişkeni oluşturuldu
//  2. Clients.All.SendAsync(OrderItemStatusChanged) → Clients.Group(tenantId)
//  3. Clients.All.SendAsync(OrderReadyForPickup)    → Clients.Group(tenantId)
//
//  DİĞER TÜM SATIRLAR AYNEN KORUNDU.
// ============================================================================
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared.Common;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Controllers
{
    [Authorize(Roles = "Admin,Kitchen")]
    public class KitchenController : Controller
    {
        private readonly RestaurantDbContext _db;
        private readonly IHubContext<RestaurantHub> _hub;

        public KitchenController(RestaurantDbContext db, IHubContext<RestaurantHub> hub)
        {
            _db = db;
            _hub = hub;
        }

        // ─────────────────────────────────────────────────────────────
        // GET /Kitchen/Display
        // ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> Display()
        {
            var orders = await _db.Orders
                .Where(o => o.OrderStatus == OrderStatus.Open)
                .Include(o => o.Table)
                .Include(o => o.OrderItems
                    .Where(oi => oi.OrderItemStatus == OrderItemStatus.Pending
                              || oi.OrderItemStatus == OrderItemStatus.Preparing
                              || oi.OrderItemStatus == OrderItemStatus.Ready))
                    .ThenInclude(oi => oi.MenuItem)
                .OrderBy(o => o.OrderOpenedAt)
                .ToListAsync();

            orders = orders.Where(o => o.OrderItems.Any()).ToList();
            return View(orders);
        }

        // ─────────────────────────────────────────────────────────────
        // GET /Kitchen/GetOrderCardPartial?orderId=42
        // SignalR NewOrderItem/OrderUpdated tetiklenince JS bu endpoint'i
        // çağırır; güncel kart HTML'ini PartialView olarak döner.
        // ─────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetOrderCardPartial(int orderId)
        {
            var order = await _db.Orders
                .Where(o => o.OrderId == orderId && o.OrderStatus == OrderStatus.Open)
                .Include(o => o.Table)
                .Include(o => o.OrderItems
                    .Where(oi => oi.OrderItemStatus == OrderItemStatus.Pending
                              || oi.OrderItemStatus == OrderItemStatus.Preparing
                              || oi.OrderItemStatus == OrderItemStatus.Ready))
                    .ThenInclude(oi => oi.MenuItem)
                .FirstOrDefaultAsync();

            // Adisyon artık kapalıysa ya da mutfak kalemi kalmadıysa boş döner;
            // JS tarafı kartı DOM'dan kaldırır.
            if (order == null || !order.OrderItems.Any())
                return Content(string.Empty);

            return PartialView("_OrderCard", order);
        }

        // ─────────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> UpdateStatus([FromBody] KdsStatusUpdateDto dto)
        {
            if (dto is null)
                return BadRequest(new { message = "Geçersiz istek gövdesi." });

            var item = await _db.OrderItems
                .Include(oi => oi.Order).ThenInclude(o => o.Table)
                .Include(oi => oi.MenuItem)
                .FirstOrDefaultAsync(oi => oi.OrderItemId == dto.OrderItemId);

            if (item is null)
                return NotFound(new { message = "Sipariş kalemi bulunamadı." });

            // dto.NewStatus HTTP'den string geliyor; önce enum'a parse et
            if (!Enum.TryParse<OrderItemStatus>(dto.NewStatus, ignoreCase: true, out var parsedNew))
                return BadRequest(new { message = $"Geçersiz durum değeri: '{dto.NewStatus}'" });

            bool gecerli = (item.OrderItemStatus == OrderItemStatus.Pending && parsedNew == OrderItemStatus.Preparing)
                        || (item.OrderItemStatus == OrderItemStatus.Preparing && parsedNew == OrderItemStatus.Ready)
                        || (item.OrderItemStatus == OrderItemStatus.Ready && parsedNew == OrderItemStatus.Served);

            if (!gecerli)
                return BadRequest(new
                {
                    message = $"Geçersiz geçiş: '{item.OrderItemStatus}' → '{dto.NewStatus}'"
                });

            var tableName = item.Order?.Table?.TableName ?? $"Adisyon #{item.OrderId}";
            var menuItemName = item.MenuItem?.MenuItemName ?? "Ürün";
            var tenantId = item.Order?.TenantId ?? "";

            // ── [SPRINT-4] Ready statüsü artık DB'de Ready olarak kalır.
            // Garson siparişi fiziksel olarak götürüp "Servis Edildi" diyene kadar
            // (sayfayı yenilese bile) masa kartındaki 'Hazır' rozeti görünür.
            // Eskisi: Ready gelince direkt Served yapılıyordu → rozet kayboluyordu.
            item.OrderItemStatus = parsedNew; // Ready → Ready, Preparing → Preparing
            await _db.SaveChangesAsync();

            // KDS kart yenilemesi
            await _hub.Clients.Group(tenantId).SendAsync("OrderItemStatusChanged", new
            {
                orderItemId = item.OrderItemId,
                orderId = item.OrderId,
                newStatus = dto.NewStatus,
                tableName,
                menuItemName
            });

            if (parsedNew == OrderItemStatus.Ready)
            {
                // Garson ekranındaki masa kartına kalıcı 'Hazır' rozeti için
                await _hub.Clients.Group(tenantId).SendAsync("OrderReady", new
                {
                    orderId = item.OrderId,
                    tableId = item.Order?.Table?.TableId ?? 0,
                    tableName,
                    menuItemName,
                    readyAt = DateTime.Now.ToString("HH:mm")
                });
            }
            else if (parsedNew == OrderItemStatus.Served)
            {
                // Ready rozeti kaldırılsın — hâlâ Ready kalem var mı?
                bool stillHasReady = await _db.OrderItems.AnyAsync(oi =>
                    oi.OrderId == item.OrderId &&
                    oi.OrderItemStatus == OrderItemStatus.Ready &&
                    oi.OrderItemId != item.OrderItemId);

                if (!stillHasReady)
                {
                    await _hub.Clients.Group(tenantId).SendAsync("OrderServed", new
                    {
                        orderId = item.OrderId,
                        tableId = item.Order?.Table?.TableId ?? 0,
                        tableName
                    });
                }

                // KDS'te artık Pending/Preparing/Ready kalem kaldı mı?
                bool hasKitchenItems = await _db.OrderItems.AnyAsync(oi =>
                    oi.OrderId == item.OrderId &&
                    oi.OrderItemId != item.OrderItemId &&
                    (oi.OrderItemStatus == OrderItemStatus.Pending ||
                     oi.OrderItemStatus == OrderItemStatus.Preparing ||
                     oi.OrderItemStatus == OrderItemStatus.Ready));

                if (!hasKitchenItems)
                    await _hub.Clients.Group(tenantId).SendAsync("RemoveOrderCard", new { orderId = item.OrderId });
                else
                    await _hub.Clients.Group(tenantId).SendAsync("OrderUpdated", new { orderId = item.OrderId });
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
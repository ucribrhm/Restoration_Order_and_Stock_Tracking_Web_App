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
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Controllers
{
    [AllowAnonymous]
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
                .Where(o => o.OrderStatus == "open")
                .Include(o => o.Table)
                .Include(o => o.OrderItems
                    .Where(oi => oi.OrderItemStatus == "pending" || oi.OrderItemStatus == "preparing"))
                    .ThenInclude(oi => oi.MenuItem)
                .OrderBy(o => o.OrderOpenedAt)
                .ToListAsync();

            orders = orders.Where(o => o.OrderItems.Any()).ToList();
            return View(orders);
        }

        // ─────────────────────────────────────────────────────────────
        // POST /Kitchen/UpdateStatus
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

            bool gecerli = (item.OrderItemStatus == "pending" && dto.NewStatus == "preparing")
                        || (item.OrderItemStatus == "preparing" && dto.NewStatus == "ready");

            if (!gecerli)
                return BadRequest(new
                {
                    message = $"Geçersiz geçiş: '{item.OrderItemStatus}' → '{dto.NewStatus}'"
                });

            var tableName = item.Order?.Table?.TableName ?? $"Adisyon #{item.OrderId}";
            var menuItemName = item.MenuItem?.MenuItemName ?? "Ürün";

            // ── [SIG] TenantId DB nesnesinden oku ──────────────────────────
            // KitchenController [AllowAnonymous] → ITenantService.TenantId null döner.
            // Order.TenantId (FAZ 1 ADIM 2'de eklendi) doğrudan kullanılır.
            var tenantId = item.Order?.TenantId ?? "";
            // ──────────────────────────────────────────────────────────────

            item.OrderItemStatus = dto.NewStatus == "ready" ? "served" : dto.NewStatus;
            await _db.SaveChangesAsync();

            // ── [SIG] Clients.All → Clients.Group(tenantId) ───────────────
            await _hub.Clients.Group(tenantId).SendAsync("OrderItemStatusChanged", new
            {
                orderItemId = item.OrderItemId,
                newStatus = dto.NewStatus,
                tableName,
                menuItemName
            });

            if (dto.NewStatus == "ready")
            {
                await _hub.Clients.Group(tenantId).SendAsync("OrderReadyForPickup", new
                {
                    orderItemId = item.OrderItemId,
                    orderId = item.OrderId,
                    tableName,
                    menuItemName,
                    readyAt = DateTime.Now.ToString("HH:mm:ss")
                });
            }
            // ──────────────────────────────────────────────────────────────

            return Ok(new { success = true, tableName, menuItemName });
        }
    }

    public class KdsStatusUpdateDto
    {
        public int OrderItemId { get; set; }
        public string NewStatus { get; set; } = string.Empty;
    }
}
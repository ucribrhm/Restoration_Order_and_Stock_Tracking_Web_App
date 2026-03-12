// ============================================================================
//  Controllers/KitchenController.cs  —  KDS v3
//  SPRINT 3 DEĞİŞİKLİĞİ: Display() metoduna "ros-tenant" cookie eki
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
                .Where(o => o.OrderStatus == OrderStatus.Open)
                .Include(o => o.Table)
                .Include(o => o.OrderItems
                    .Where(oi => oi.OrderItemStatus == OrderItemStatus.Pending || oi.OrderItemStatus == OrderItemStatus.Preparing))
                    .ThenInclude(oi => oi.MenuItem)
                .OrderBy(o => o.OrderOpenedAt)
                .ToListAsync();

            orders = orders.Where(o => o.OrderItems.Any()).ToList();

            // ── [KDS-COOKIE] TenantId'yi cookie olarak yaz ───────────────────
            // RestaurantHub [AllowAnonymous] bağlantılarında Claims boş gelir.
            // Cookie'den TenantId okunarak KDS doğru tenant grubuna eklenir.
            var tenantId = orders.FirstOrDefault()?.TenantId;
            if (!string.IsNullOrEmpty(tenantId))
            {
                Response.Cookies.Append("ros-tenant", tenantId, new CookieOptions
                {
                    HttpOnly = false,                         // JS ile okunabilsin
                    Secure = false,                         // Geliştirme ortamı
                    SameSite = SameSiteMode.Strict,
                    Expires = DateTimeOffset.UtcNow.AddHours(8)
                });
            }
            // ─────────────────────────────────────────────────────────────────

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

            if (!Enum.TryParse<OrderItemStatus>(dto.NewStatus, ignoreCase: true, out var parsedNew))
                return BadRequest(new { message = $"Geçersiz durum değeri: '{dto.NewStatus}'" });

            bool gecerli = (item.OrderItemStatus == OrderItemStatus.Pending && parsedNew == OrderItemStatus.Preparing)
                        || (item.OrderItemStatus == OrderItemStatus.Preparing && parsedNew == OrderItemStatus.Ready);

            if (!gecerli)
                return BadRequest(new
                {
                    message = $"Geçersiz geçiş: '{item.OrderItemStatus}' → '{dto.NewStatus}'"
                });

            var tableName = item.Order?.Table?.TableName ?? $"Adisyon #{item.OrderId}";
            var menuItemName = item.MenuItem?.MenuItemName ?? "Ürün";

            // KitchenController [AllowAnonymous] → ITenantService.TenantId null döner.
            // Order.TenantId doğrudan kullanılır.
            var tenantId = item.Order?.TenantId ?? "";

            item.OrderItemStatus = parsedNew == OrderItemStatus.Ready ? OrderItemStatus.Served : parsedNew;
            await _db.SaveChangesAsync();

            await _hub.Clients.Group(tenantId).SendAsync("OrderItemStatusChanged", new
            {
                orderItemId = item.OrderItemId,
                newStatus = dto.NewStatus,
                tableName,
                menuItemName
            });

            if (parsedNew == OrderItemStatus.Ready)
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

            return Ok(new { success = true, tableName, menuItemName });
        }
    }

    public class KdsStatusUpdateDto
    {
        public int OrderItemId { get; set; }
        public string NewStatus { get; set; } = string.Empty;
    }
}
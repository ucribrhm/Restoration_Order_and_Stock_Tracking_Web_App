using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Controllers
{
    /// <summary>
    /// Müşterilerin QR kod ile açtığı Fine-Dining menü ekranı.
    /// Kimlik doğrulama gerektirmez — [AllowAnonymous] davranışı varsayılandır.
    /// URL örneği: /QrMenu/Index/Masa-1
    /// </summary>
    public class QrMenuController : Controller
    {
        private readonly RestaurantDbContext _context;
        private readonly IHubContext<RestaurantHub> _hub;

        public QrMenuController(RestaurantDbContext context, IHubContext<RestaurantHub> hub)
        {
            _context = context;
            _hub = hub;
        }

        // ── GET /QrMenu/Index/{tableName} ──────────────────────────────────────
        [HttpGet]
        [Route("QrMenu/Index/{tableName}")]
        public async Task<IActionResult> Index(string tableName)
        {
            var decodedName = Uri.UnescapeDataString(tableName);

            var table = await _context.Tables
                .FirstOrDefaultAsync(t => t.TableName == decodedName);

            if (table == null)
                return NotFound("Bu QR koda ait masa bulunamadı.");

            var categories = await _context.Categories
                .Where(c => c.IsActive)
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
            ViewData["IsWaiterCalled"] = table.IsWaiterCalled;

            return View(categories);
        }

        // ── POST /QrMenu/CallWaiter ────────────────────────────────────────────
        [HttpPost]
        [IgnoreAntiforgeryToken]
        [Route("QrMenu/CallWaiter")]
        public async Task<IActionResult> CallWaiter([FromBody] CallWaiterRequest request)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.TableName))
                return BadRequest(new { success = false, message = "Geçersiz masa adı." });

            var table = await _context.Tables
                .FirstOrDefaultAsync(t => t.TableName == request.TableName);

            if (table == null)
                return NotFound(new { success = false, message = "Masa bulunamadı." });

            // Zaten çağrıldıysa tekrar işlem yapma — sadece onayla
            if (table.IsWaiterCalled)
                return Ok(new { success = true, alreadyCalled = true, message = "Garson zaten çağrıldı." });

            table.IsWaiterCalled = true;
            table.WaiterCalledAt = DateTime.UtcNow;   // ← YENİ: SLA saatini kaydet
            await _context.SaveChangesAsync();

            // SignalR: Tüm bağlı garson / admin ekranlarına anlık bildir
            // calledAtUtc alanını da iletiyoruz — Tables/Index.js SLA timer için kullanır
            await _hub.Clients.All.SendAsync("WaiterCalled", new
            {
                tableName = table.TableName,
                calledAtUtc = table.WaiterCalledAt!.Value.ToString("o") // ISO 8601
            });

            return Ok(new { success = true, alreadyCalled = false, message = "Garson çağrıldı." });
        }
    }

    /// <summary>Müşteri tarafından gönderilen CallWaiter istek gövdesi.</summary>
    public class CallWaiterRequest
    {
        public string TableName { get; set; } = string.Empty;
    }
}
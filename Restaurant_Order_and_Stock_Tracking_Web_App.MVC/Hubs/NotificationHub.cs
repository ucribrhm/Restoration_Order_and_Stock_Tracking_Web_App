// ============================================================================
//  Hubs/NotificationHub.cs
//
//  SPRINT FAZ 1 (korundu):
//  [SIG-1] OnConnectedAsync  → bağlanan client'ı tenant grubuna ekle
//  [SIG-2] OnDisconnectedAsync → ayrılan client'ı gruptan çıkar
//
//  DÜZELTME FAZI 2:
//  [SEC-04] OnConnectedAsync → Claims'ten okunan TenantId artık DB'den
//           doğrulanıyor (RestaurantHub ile eşitlendi).
//           Pasif veya var olmayan tenant → Context.Abort().
//           Gerekçe: Manipüle edilmiş/süresi dolmuş cookie/claim ile
//           dashboard bildirimlerine erişim engellendi.
// ============================================================================
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using System.Security.Claims;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs
{
    // [FIX-401] AuthenticationSchemes = "AppAuth" eklendi.
    // Varsayılan scheme bırakıldığında .NET, Identity'nin default scheme'ini
    // (AddIdentity → "Identity.Application") deniyor; bu scheme SignalR
    // negotiate isteğindeki "RestaurantOS.AppAuth" cookie'yi tanımıyor → 401.
    // AppAuth açıkça belirtilince doğru cookie okunur, 401 düzelir.
    [Authorize(AuthenticationSchemes = "AppAuth")]
    public class NotificationHub : Hub
    {
        // [SEC-04] Tenant doğrulaması için DbContext inject edildi.
        // Hub'ın lifetime'ı per-connection olduğundan Scoped DbContext uygundur.
        private readonly RestaurantDbContext _db;
        private readonly ILogger<NotificationHub> _logger;

        public NotificationHub(RestaurantDbContext db, ILogger<NotificationHub> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ── [SIG-1] Bağlantı Kurulunca — Tenant Doğrula + Gruba Ekle ────────
        public override async Task OnConnectedAsync()
        {
            // [FIX-TENANT] TenantId çözümleme sırası:
            // 1. Claims (normal login — en güvenilir)
            // 2. QueryString ?tenantId=xxx  (SignalR negotiate URL'inden)
            // 3. Cookie "ros-tenant"         (fallback)
            var tenantId = Context.User?.FindFirstValue("TenantId");

            if (string.IsNullOrEmpty(tenantId))
                tenantId = Context.GetHttpContext()?.Request.Query["tenantId"].ToString();

            if (string.IsNullOrEmpty(tenantId))
                tenantId = Context.GetHttpContext()?.Request.Cookies["ros-tenant"];

            if (string.IsNullOrEmpty(tenantId))
            {
                // TenantId hiçbir yerden çözülemediyse (SysAdmin vb.) → lobi.
                // Bağlantıyı kesmiyoruz; bildirim almaz ama çökmez.
                await base.OnConnectedAsync();
                return;
            }

            // [SEC-04] Claims'ten okunan TenantId'nin DB'de gerçekten aktif
            // bir tenant'a karşılık geldiğini doğrula (RestaurantHub ile eşit koruma).
            // Bu kontrol olmadan pasif/silinmiş tenant claims'i ile
            // dashboard bildirimlerine erişim mümkündü.
            var tenantExists = await _db.Tenants
                .AnyAsync(t => t.TenantId == tenantId && t.IsActive);

            if (!tenantExists)
            {
                _logger.LogWarning(
                    "[NotificationHub] Geçersiz veya pasif TenantId '{TenantId}' — " +
                    "ConnectionId: {ConnectionId}. Bağlantı reddedildi.",
                    tenantId, Context.ConnectionId);

                Context.Abort();
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, tenantId);

            _logger.LogInformation(
                "[NotificationHub] Bağlandı — ConnectionId: {ConnectionId} → Group: {TenantId}",
                Context.ConnectionId, tenantId);

            await base.OnConnectedAsync();
        }

        // ── [SIG-2] Bağlantı Kesilince — Gruptan Çıkar ──────────────────────
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var tenantId = Context.User?.FindFirstValue("TenantId");

            if (!string.IsNullOrEmpty(tenantId))
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, tenantId);

            await base.OnDisconnectedAsync(exception);
        }

        // Hub metodları intentionally boş.
        // Tüm broadcast'ler IHubContext<NotificationHub> üzerinden yapılır.
    }
}
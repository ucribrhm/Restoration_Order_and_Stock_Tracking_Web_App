// ============================================================================
//  Hubs/RestaurantHub.cs
//  SPRINT 3 — KDS Tenant İzolasyon Bugfix
//
//  DEĞİŞİKLİKLER:
//  [SIG-3] ILogger<RestaurantHub> inject edildi
//  [SIG-4] OnConnectedAsync → Claims boşsa Cookie fallback ("ros-tenant")
//  [SIG-5] OnDisconnectedAsync → aynı fallback mantığı
//
//  KDS [AllowAnonymous] ÇÖZÜMÜ:
//    KitchenController.Display() → Response.Cookies.Append("ros-tenant", tenantId)
//    RestaurantHub.OnConnectedAsync() → Cookie'den tenantId okur, gruba ekler
//    Böylece KDS ekranı da NewOrderItem / OrderItemStatusChanged eventlerini alır.
// ============================================================================
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs
{
    /// <summary>
    /// Restoran genelindeki gerçek zamanlı bildirimler için SignalR Hub.
    /// Desteklenen event'ler (server → client):
    ///   • WaiterCalled           – müşteri "Garson Çağır"a bastı
    ///   • WaiterDismissed        – garson "İlgilenildi"ye bastı
    ///   • OrderItemStatusChanged – KDS item durumu değişti
    ///   • NewOrderItem           – garson yeni ürün ekledi → KDS'e düşer
    ///   • OrderReadyForPickup    – mutfak "Hazır" bastı → garson bildirim alır
    /// </summary>
    public class RestaurantHub : Hub
    {
        // ── [SIG-3] Logger inject ────────────────────────────────────────────
        private readonly ILogger<RestaurantHub> _logger;

        public RestaurantHub(ILogger<RestaurantHub> logger)
        {
            _logger = logger;
        }

        // ── [SIG-4] Bağlantı Kurulunca — Tenant Grubuna Ekle ────────────────
        public override async Task OnConnectedAsync()
        {
            // Önce Claims'ten TenantId oku (kimlikli kullanıcılar — garson, kasiyer, admin)
            var tenantId = Context.User?.FindFirstValue("TenantId");

            // [KDS BUGFIX] Claims boşsa (AllowAnonymous → KitchenController):
            // KitchenController.Display() bu cookie'yi set eder (HttpOnly=false)
            if (string.IsNullOrEmpty(tenantId))
            {
                tenantId = Context.GetHttpContext()?.Request.Cookies["ros-tenant"];
            }

            if (!string.IsNullOrEmpty(tenantId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, tenantId);
                _logger.LogInformation(
                    "[RestaurantHub] Bağlandı — ConnectionId: {ConnectionId} → Group: {TenantId}",
                    Context.ConnectionId, tenantId);
            }
            else
            {
                // TenantId bulunamadı — gruba eklenmiyor (kasıtlı)
                _logger.LogWarning(
                    "[RestaurantHub] TenantId yok — ConnectionId: {ConnectionId} gruba eklenmedi.",
                    Context.ConnectionId);
            }

            await base.OnConnectedAsync();
        }

        // ── [SIG-5] Bağlantı Kesilince — Gruptan Çıkar ──────────────────────
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // Claims'ten oku
            var tenantId = Context.User?.FindFirstValue("TenantId");

            // Cookie fallback (KDS için)
            if (string.IsNullOrEmpty(tenantId))
            {
                tenantId = Context.GetHttpContext()?.Request.Cookies["ros-tenant"];
            }

            if (!string.IsNullOrEmpty(tenantId))
            {
                // SignalR ayrılan connection'ı otomatik temizler,
                // ancak açık çağrı daha deterministik davranış sağlar.
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, tenantId);
                _logger.LogInformation(
                    "[RestaurantHub] Bağlantı kesildi — ConnectionId: {ConnectionId}, Group: {TenantId}",
                    Context.ConnectionId, tenantId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        // Hub metodları intentionally boş:
        // tüm broadcast'ler server-side (controller içinden) IHubContext<RestaurantHub>
        // aracılığıyla yapılır; istemciden hub'a doğrudan çağrı gerekmez.
    }
}
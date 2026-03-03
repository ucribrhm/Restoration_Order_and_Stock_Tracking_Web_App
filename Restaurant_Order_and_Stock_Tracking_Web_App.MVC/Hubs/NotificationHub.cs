// ============================================================================
//  Hubs/NotificationHub.cs
//  DEĞİŞİKLİK — FAZ 1 ADIM 3: SignalR Tenant İzolasyonu
//
//  EKLENENLER:
//  [SIG-1] OnConnectedAsync  → bağlanan client'ı tenant grubuna ekle
//  [SIG-2] OnDisconnectedAsync → ayrılan client'ı gruptan çıkar
//
//  [Authorize] korundu → yalnızca kimliği doğrulanmış kullanıcılar bağlanır.
//  Bu sayede TenantId Claims'te her zaman mevcuttur (TenantClaimsTransformation
//  tarafından login sırasında eklendi).
// ============================================================================
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs
{
    [Authorize]
    public class NotificationHub : Hub
    {
        // ── [SIG-1] Bağlantı Kurulunca — Tenant Grubuna Ekle ────────────────
        public override async Task OnConnectedAsync()
        {
            var tenantId = Context.User?.FindFirstValue("TenantId");

            if (!string.IsNullOrEmpty(tenantId))
                await Groups.AddToGroupAsync(Context.ConnectionId, tenantId);

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
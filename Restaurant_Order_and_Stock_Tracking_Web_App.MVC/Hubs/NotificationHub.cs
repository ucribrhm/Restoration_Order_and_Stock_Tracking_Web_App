// ════════════════════════════════════════════════════════════════════════════
//  Hubs/NotificationHub.cs
//  Yol: Restaurant_Order_and_Stock_Tracking_Web_App.MVC/Hubs/
//
//  Dashboard'a gerçek zamanlı operasyon bildirimleri gönderen SignalR Hub.
//
//  ─── Event Listesi (Server → Client) ────────────────────────────────────
//  ReceiveNotification   { icon, message, color, orderId?, tableName }
//
//  ─── Mevcut RestaurantHub ile İlişkisi ──────────────────────────────────
//  RestaurantHub  → WaiterCalled / WaiterDismissed  (masa bazlı anlık)
//  NotificationHub → ReceiveNotification            (dashboard operasyon log)
//  İkisi bağımsız çalışır; birbirini etkilemez.
//
//  ─── Güvenlik ────────────────────────────────────────────────────────────
//  [Authorize] → yalnızca giriş yapmış kullanıcılar bağlanabilir.
//  Tüm broadcast'ler server-side (controller içinden) IHubContext<> ile
//  yapılır; istemciden hub'a doğrudan çağrı gerekmez.
// ════════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs
{
    [Authorize]
    public class NotificationHub : Hub
    {
        // Hub metodları intentionally boş.
        // Tüm broadcast'ler OrdersController (ve ileride diğer controller'lar)
        // tarafından IHubContext<NotificationHub> üzerinden yapılır.
    }
}
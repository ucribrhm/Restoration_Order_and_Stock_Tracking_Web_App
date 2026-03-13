// ============================================================================
//  Hubs/RestaurantHub.cs  —  v3  "Bulletproof Handshake"
//
//  KÖK NEDEN (neden sistem "sağır"dı):
//  ─────────────────────────────────────────────────────────────────────────
//  v2'de TenantId çözümleme sırası: Claims → Cookie
//
//  SORUN-1 (Kimlikli kullanıcılar — Garson/Admin):
//    AppAuth cookie'si var FAKAT _AppLayout.cshtml'de SignalR bağlantısı
//    .withUrl('/hubs/restaurant') ile kuruluyordu — QueryString YOK.
//    Hub, Claims'ten TenantId alabiliyordu. ANCAK:
//    _AppLayout'ta window.APP_TENANT_ID tanımlı DEĞİLDİ.
//    Tables/Index.js bağlantıyı withUrl('/hubs/restaurant') ile kuruyordu,
//    Claims dolu olduğundan Garson için çalışıyor GÖRÜNÜYORDU.
//    Gerçek problem: "AppAuth" cookie'sinin SignalR WebSocket handshake'inde
//    Claims'i taşıyıp taşımadığı middleware pipeline'ına bağlı.
//
//  SORUN-2 (Anonim KDS — Kitchen rolü yoksa):
//    KDS [AllowAnonymous], Claims KESİNLİKLE boş.
//    Cookie "ros-tenant" KitchenController.Display() tarafından set ediliyor.
//    Display.cshtml'deki SignalR bağlantısı da .withUrl('/hubs/restaurant')
//    ile kuruluyordu — QueryString YOK.
//    Şans eseri Cookie çalışıyordu, ama QueryString yoksa ilk yükleme
//    cookie set edilmeden Hub'a bağlanılıyorsa Abort() devreye giriyordu.
//
//  SORUN-3 (QR Menü — Tamamen anonim):
//    QR Menü'de SignalR kodu YOKTU. WaiterDismissed hiç dinlenmiyordu.
//    "Garson Çağrıldı" butonu F5'e kadar sıfırlanamıyordu.
//
//  V3 ÇÖZÜMÜ — 3 Katmanlı Fallback:
//  ─────────────────────────────────────────────────────────────────────────
//  1. Claims     → Kimlikli Garson/Admin (en güvenilir)
//  2. Cookie     → KDS tableti (Display() sonrası set edilir, HttpOnly=false)
//  3. QueryString (?tenantId=X) → Herhangi bir istemci için son çare
//
//  JS tarafında URL formatı:
//    .withUrl(`/hubs/restaurant?tenantId=${encodeURIComponent(tenantId)}`)
//
//  WebSocket, LongPolling, SSE transport'larının TAMAMINDA QueryString korunur.
//  Bu nedenle QueryString en evrensel ve güvenilir yöntemdir.
//
//  GÜVENLİK:
//    Claims: Login sırasında doğrulandı → DB sorgusu ATLA
//    Cookie + QueryString: Manipülasyon riski → her bağlantıda DB doğrula
//    TenantId public bir sır değildir (QR URL'de zaten var), asıl izolasyon
//    DB doğrulamasından ve Groups mekanizmasından gelir.
// ============================================================================

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using System.Security.Claims;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs
{
    public class RestaurantHub : Hub
    {
        private readonly ILogger<RestaurantHub> _logger;
        private readonly RestaurantDbContext _db;

        public RestaurantHub(ILogger<RestaurantHub> logger, RestaurantDbContext db)
        {
            _logger = logger;
            _db = db;
        }

        // ── TenantId'yi 3 Katmanlı Fallback ile Çöz ─────────────────────────
        private (string? tenantId, string source) ResolveTenantId()
        {
            var http = Context.GetHttpContext();

            // Katman 1: Claims — Kimlikli Garson/Admin (AppAuth cookie'sinden gelir)
            var fromClaims = Context.User?.FindFirstValue("TenantId");
            if (!string.IsNullOrWhiteSpace(fromClaims))
                return (fromClaims.Trim(), "claims");

            // Katman 2: Cookie — KDS tableti (KitchenController.Display tarafından set edilir)
            // HttpOnly=false olarak set edilir ki SignalR WebSocket handshake'i okuyabilsin
            var fromCookie = http?.Request.Cookies["ros-tenant"];
            if (!string.IsNullOrWhiteSpace(fromCookie))
                return (fromCookie.Trim(), "cookie");

            // Katman 3: QueryString — Her istemci için evrensel yöntem
            // JS: .withUrl(`/hubs/restaurant?tenantId=${encodeURIComponent(tenantId)}`)
            // WebSocket handshake'i HTTP upgrade isteğidir → QueryString korunur
            var fromQuery = http?.Request.Query["tenantId"].ToString();
            if (!string.IsNullOrWhiteSpace(fromQuery))
                return (fromQuery.Trim(), "querystring");

            return (null, "none");
        }

        // ── OnConnectedAsync ─────────────────────────────────────────────────
        public override async Task OnConnectedAsync()
        {
            var connId = Context.ConnectionId;
            var (tenantId, source) = ResolveTenantId();

            // TenantId hiçbir kaynaktan gelmedi
            if (string.IsNullOrEmpty(tenantId))
            {
                var isAuthenticated = Context.User?.Identity?.IsAuthenticated == true;

                if (isAuthenticated)
                {
                    // SysAdmin: TenantId kasıtlı yok, bağlantıyı açık tut
                    _logger.LogInformation(
                        "[Hub] SysAdmin bağlandı (TenantId'siz) — grup katılımı atlandı. ConnId: {C}", connId);
                    await base.OnConnectedAsync();
                    return;
                }

                // Anonim + TenantId yok = geçersiz bağlantı
                // Bu durum normalde olmamalı; JS her zaman ?tenantId= göndermelidir.
                _logger.LogWarning(
                    "[Hub] TenantId bulunamadı (kaynak: none) — bağlantı reddedildi. ConnId: {C}", connId);
                Context.Abort();
                return;
            }

            // DB Doğrulaması
            // Claims: Login sırasında zaten doğrulandı → atla (performans)
            // Cookie + QueryString: Her bağlantıda doğrula (güvenlik)
            if (source != "claims")
            {
                var tenantExists = await _db.Tenants
                    .AnyAsync(t => t.TenantId == tenantId && t.IsActive);

                if (!tenantExists)
                {
                    _logger.LogWarning(
                        "[Hub] Geçersiz/pasif TenantId '{T}' (kaynak: {S}) — reddedildi. ConnId: {C}",
                        tenantId, source, connId);
                    Context.Abort();
                    return;
                }
            }

            // Tenant grubuna ekle
            await Groups.AddToGroupAsync(connId, tenantId);

            _logger.LogInformation(
                "[Hub] ✅ Bağlandı — ConnId: {C} → Group: '{T}' (kaynak: {S})",
                connId, tenantId, source);

            await base.OnConnectedAsync();
        }

        // ── OnDisconnectedAsync ──────────────────────────────────────────────
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var connId = Context.ConnectionId;
            var (tenantId, _) = ResolveTenantId();

            if (!string.IsNullOrEmpty(tenantId))
            {
                await Groups.RemoveFromGroupAsync(connId, tenantId);
                _logger.LogInformation(
                    "[Hub] Kesildi — ConnId: {C}, Group: '{T}'", connId, tenantId);
            }
            else
            {
                _logger.LogInformation(
                    "[Hub] Kesildi — ConnId: {C} (grup üyesi değildi)", connId);
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
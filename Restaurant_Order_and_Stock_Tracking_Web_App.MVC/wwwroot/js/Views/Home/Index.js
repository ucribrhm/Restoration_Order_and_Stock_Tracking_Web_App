// ============================================================================
//  wwwroot/js/Views/Home/Index.js  —  v2 Bulletproof
//  Dashboard Canlı Güncelleme
//
//  1. NotificationHub → "Sipariş Adedi" KPI sayacı + bildirim toast'ı
//  2. RestaurantHub   → "Hazır" sipariş toast bildirimi
//
//  DÜZELTMELER:
//  [FIX-1] LogLevel.Warning → LogLevel.Information  (sessiz hata ayıklama sona erdi)
//  [FIX-2] Tüm handler'lara try-catch eklendi       (ilk TypeError listener'ı öldürüyordu)
//  [FIX-3] Payload camelCase/PascalCase çift okuma  (C# serialize tutarsızlığına karşı)
//  [FIX-4] DOM element null-check sıkılaştırıldı    (element yoksa sessiz return)
//  [FIX-5] Her handler'a console.info eklendi        (tarayıcı konsolundan debug)
//  [FIX-6] restaurantConn URL'e APP_TENANT_ID eklendi (RestaurantHub v3 pattern)
//  [FIX-7] getOrderKval() → data-kpi="open-orders" ile direkt ID'li okuma
// ============================================================================

(function initDashboardLive() {

    // ── APP_TENANT_ID: _AppLayout.cshtml'de meta tag olarak set edilir ────────
    // window.APP_TENANT_ID = document.querySelector('meta[name="ros-tenant-id"]').content
    const tenantId = (window.APP_TENANT_ID || '').trim();

    // ════════════════════════════════════════════════════════════════════════
    // BÖLÜM 1 — NotificationHub: KPI Sayacı + Toast Güncelleme
    // ════════════════════════════════════════════════════════════════════════

    // NotificationHub [Authorize] → AppAuth cookie WS handshake'inde taşınır
    // → Claims otomatik dolar → TenantId okunur → gruba eklenir.
    // tenantId'yi URL'e eklemek GEREKMEZ ama zarar vermez.
    const notifConn = new signalR.HubConnectionBuilder()
        .withUrl('/notificationHub')
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Information) // [FIX-1] Warning → Information
        .build();

    // [FIX-7] Sayaç elementini data-kpi attribute ile bul (metin aramadan daha güvenilir)
    // Index.cshtml'de <span data-kpi="open-orders"> şeklinde olmalı.
    // Eğer bu attribute yoksa .kval[data-cu] içinden "Sipariş" içeren kart aranır (fallback).
    function getOrderKval() {
        // Birincil: data-kpi="open-orders" ile direkt element
        const direct = document.querySelector('[data-kpi="open-orders"]');
        if (direct) return direct;

        // Fallback: kart başlığı "Sipariş" içeren .kval[data-cu]
        let found = null;
        document.querySelectorAll('.kval[data-cu]').forEach(el => {
            const card = el.closest('.kc');
            if (!card) return;
            const lbl = card.querySelector('.klbl');
            if (lbl && lbl.textContent.trim().includes('Sipariş')) found = el;
        });
        return found;
    }

    // Animasyonlu sayaç artış/azalış
    function bumpCounter(el, delta) {
        const current = parseInt(el.dataset.cu || el.textContent) || 0;
        const next = Math.max(0, current + delta);
        el.dataset.cu = next;

        const t0 = performance.now();
        const from = current;
        (function step(ts) {
            const p = Math.min((ts - t0) / 600, 1);
            const e = 1 - Math.pow(1 - p, 3);
            el.textContent = Math.round(from + e * (next - from));
            if (p < 1) requestAnimationFrame(step);
        })(performance.now());

        el.classList.add('count-bump');
        setTimeout(() => el.classList.remove('count-bump'), 500);
    }

    // [FIX-2] try-catch eklendi — payload bozuksa handler ölmüyor
    // [FIX-3] camelCase/PascalCase çift okuma
    // [FIX-5] console.info eklendi
    notifConn.on('ReceiveNotification', function (payload) {
        try {
            console.info('[SignalR Dashboard] ReceiveNotification geldi:', payload);

            if (!payload) return; // [FIX-4] null-guard

            // [FIX-3] camelCase → PascalCase fallback
            const icon = payload.icon ?? payload.Icon ?? '';

            const kval = getOrderKval();

            if (icon === '🧾') {
                // Yeni adisyon açıldı → açık sipariş sayısı artar
                if (kval) bumpCounter(kval, 1);
            } else if (icon === '✅') {
                // Hesap kapandı → açık sipariş sayısı azalır
                if (kval) bumpCounter(kval, -1);
            }
        } catch (err) {
            // [FIX-2] Handler'ın ölmesini engelle
            console.error('[SignalR Dashboard] ReceiveNotification handler hatası:', err);
        }
    });

    notifConn.onreconnecting(err =>
        console.warn('[SignalR:Dashboard] NotificationHub yeniden bağlanıyor...', err));
    notifConn.onreconnected(cid =>
        console.info('[SignalR:Dashboard] NotificationHub yeniden bağlandı:', cid));
    notifConn.onclose(err =>
        console.error('[SignalR:Dashboard] NotificationHub kapandı:', err));

    async function startNotifConn() {
        try {
            await notifConn.start();
            console.info('[SignalR:Dashboard] NotificationHub bağlandı ✅');
        } catch (e) {
            console.error('[SignalR:Dashboard] NotificationHub bağlantı hatası:', e);
            setTimeout(startNotifConn, 5000);
        }
    }
    startNotifConn();

    // ════════════════════════════════════════════════════════════════════════
    // BÖLÜM 2 — RestaurantHub: "Hazır" Sipariş Toast
    // ════════════════════════════════════════════════════════════════════════

    // [FIX-6] RestaurantHub v3: ?tenantId= QueryString eklendi
    // Authenticated Admin için Claims zaten dolacak; bu QueryString
    // 3. katman fallback olarak da devreye girer.
    const restaurantUrl = tenantId
        ? `/hubs/restaurant?tenantId=${encodeURIComponent(tenantId)}`
        : '/hubs/restaurant';

    const restaurantConn = new signalR.HubConnectionBuilder()
        .withUrl(restaurantUrl)
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Information) // [FIX-1]
        .build();

    // [FIX-2] try-catch  [FIX-3] camelCase/PascalCase  [FIX-5] console.info
    restaurantConn.on('OrderReadyForPickup', function (payload) {
        try {
            console.info('[SignalR Dashboard] OrderReadyForPickup geldi:', payload);
            if (!payload) return; // [FIX-4]
            showDashboardReadyToast(payload);
        } catch (err) {
            console.error('[SignalR Dashboard] OrderReadyForPickup handler hatası:', err);
        }
    });

    // WaiterCalled — Dashboard'da bilgi toastı
    restaurantConn.on('WaiterCalled', function (payload) {
        try {
            console.info('[SignalR Dashboard] WaiterCalled geldi:', payload);
            if (!payload) return;
            // [FIX-3] camelCase/PascalCase fallback
            const tableName = payload.tableName ?? payload.TableName ?? '?';
            showSimpleToast('🔔', `${escHtml(tableName)} garson çağrıyor`, '#f59e0b');
        } catch (err) {
            console.error('[SignalR Dashboard] WaiterCalled handler hatası:', err);
        }
    });

    restaurantConn.onreconnecting(err =>
        console.warn('[SignalR:Dashboard] RestaurantHub yeniden bağlanıyor...', err));
    restaurantConn.onreconnected(cid =>
        console.info('[SignalR:Dashboard] RestaurantHub yeniden bağlandı:', cid));
    restaurantConn.onclose(err =>
        console.error('[SignalR:Dashboard] RestaurantHub kapandı:', err));

    async function startRestaurantConn() {
        try {
            await restaurantConn.start();
            console.info('[SignalR:Dashboard] RestaurantHub bağlandı ✅');
        } catch (e) {
            console.error('[SignalR:Dashboard] RestaurantHub bağlantı hatası:', e);
            setTimeout(startRestaurantConn, 5000);
        }
    }
    startRestaurantConn();

    // ════════════════════════════════════════════════════════════════════════
    // YARDIMCI: Hazır Sipariş Toast
    // ════════════════════════════════════════════════════════════════════════

    function showDashboardReadyToast(payload) {
        // [FIX-4] Container her seferinde DOM'dan taze okunur (IIFE başında tek seferlik değil)
        const container = document.getElementById('toast-c');
        if (!container) {
            console.warn('[SignalR Dashboard] #toast-c bulunamadı, toast gösterilemiyor');
            return;
        }

        // [FIX-3] camelCase/PascalCase çift okuma
        const orderItemId = payload.orderItemId ?? payload.OrderItemId ?? 0;
        const tableName = payload.tableName ?? payload.TableName ?? '?';
        const menuItemName = payload.menuItemName ?? payload.MenuItemName ?? '?';
        const readyAt = payload.readyAt ?? payload.ReadyAt ?? '';

        const toastId = `dash-ready-${orderItemId}`;
        if (document.getElementById(toastId)) return; // tekrar gösterme

        const toast = document.createElement('div');
        toast.id = toastId;
        toast.className = 'dashboard-ready-toast';
        toast.innerHTML =
            `<div class="drt-icon">✅</div>` +
            `<div class="drt-body">` +
            `  <div class="drt-title">${escHtml(tableName)} — Hazır!</div>` +
            `  <div class="drt-msg">${escHtml(menuItemName)} servis için hazır</div>` +
            `  <div class="drt-time">${escHtml(String(readyAt))}</div>` +
            `</div>` +
            `<button class="drt-close" onclick="this.parentElement.remove()">×</button>`;

        container.appendChild(toast);

        setTimeout(() => {
            const existing = document.getElementById(toastId);
            if (existing) {
                existing.classList.add('drt-fadeout');
                setTimeout(() => existing.remove(), 350);
            }
        }, 10000);
    }

    // Basit tek satırlık toast (WaiterCalled vb. için)
    function showSimpleToast(icon, message, color) {
        const container = document.getElementById('toast-c');
        if (!container) return; // [FIX-4]

        const t = document.createElement('div');
        t.className = 'ti';
        t.style.borderLeft = `3px solid ${color}`;
        t.innerHTML =
            `<span class="ti-ico">${icon}</span>` +
            `<span class="ti-msg">${message}</span>`;
        container.appendChild(t);

        setTimeout(() => {
            t.classList.add('out');
            setTimeout(() => t.remove(), 320);
        }, 5000);

        while (container.children.length > 4) container.firstChild.remove();
    }

    function escHtml(s) {
        if (!s) return '';
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

})();
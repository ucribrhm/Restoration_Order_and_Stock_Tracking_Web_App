// ============================================================================
//  wwwroot/js/Views/Home/Index.js  —  SPRINT 3 (YENİ DOSYA)
//  Dashboard Canlı Güncelleme
//
//  1. NotificationHub → "Sipariş Adedi" KPI sayacı canlı güncelleme
//  2. RestaurantHub   → "Hazır" sipariş toast bildirimi
//
//  NOT: Home/Index.cshtml inline script kendi NotificationHub bağlantısını
//  kuruyor (toast gösterimi için). Bu dosya ikinci bağımsız bağlantıyı kurar.
// ============================================================================

(function initDashboardLive() {

    // ════════════════════════════════════════════════════════════════════════
    // BÖLÜM 1 — NotificationHub: KPI Sayacı Güncelleme
    // ════════════════════════════════════════════════════════════════════════

    const notifConn = new signalR.HubConnectionBuilder()
        .withUrl('/notificationHub')
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    // "Sipariş Adedi" kartındaki .kval elementini bul
    // Dashboard'da klbl "Sipariş" içeren kartın data-cu'lu kval'i
    function getOrderKval() {
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

    // ReceiveNotification — SADECE sayaç güncelleme (toast cshtml inline'da)
    notifConn.on('ReceiveNotification', function (payload) {
        const kval = getOrderKval();
        if (!kval) return;

        if (payload.icon === '🧾') {
            // Yeni adisyon açıldı → toplam artar
            bumpCounter(kval, 1);
        }
        // ✅ (kapandı) durumunda TotalOrdersToday değişmez, activeOrders azalır.
        // Server-side'dan aktif sayısı push edilmediğinden sayfa yenilemesini bekle.
    });

    notifConn.onreconnecting(err =>
        console.warn('[SignalR:Dashboard] NotificationHub yeniden bağlanıyor...', err));
    notifConn.onreconnected(cid =>
        console.info('[SignalR:Dashboard] NotificationHub yeniden bağlandı:', cid));
    notifConn.onclose(err =>
        console.error('[SignalR:Dashboard] NotificationHub kapandı:', err));

    notifConn.start()
        .then(() => console.info('[SignalR:Dashboard] NotificationHub bağlandı'))
        .catch(e => console.error('[SignalR:Dashboard] NotificationHub bağlantı hatası:', e));

    // ════════════════════════════════════════════════════════════════════════
    // BÖLÜM 2 — RestaurantHub: "Hazır" Sipariş Toast
    // ════════════════════════════════════════════════════════════════════════

    const restaurantConn = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/restaurant')
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    // payload: { orderItemId, orderId, tableName, menuItemName, readyAt }
    restaurantConn.on('OrderReadyForPickup', function (payload) {
        showDashboardReadyToast(payload);
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
            console.info('[SignalR:Dashboard] RestaurantHub bağlandı');
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
        // Dashboard toast container: cshtml'de "toast-c" id'si var
        const container = document.getElementById('toast-c') || document.body;

        const toastId = `dash-ready-${payload.orderItemId}`;
        if (document.getElementById(toastId)) return;

        const toast = document.createElement('div');
        toast.id = toastId;
        toast.className = 'dashboard-ready-toast';
        toast.innerHTML = `
            <div class="drt-icon">✅</div>
            <div class="drt-body">
                <div class="drt-title">${escHtml(payload.tableName)} — Hazır!</div>
                <div class="drt-msg">${escHtml(payload.menuItemName)} servis için hazır</div>
                <div class="drt-time">${payload.readyAt || ''}</div>
            </div>
            <button class="drt-close" onclick="this.parentElement.remove()">×</button>
        `;
        container.appendChild(toast);

        setTimeout(() => {
            if (document.getElementById(toastId)) {
                toast.classList.add('drt-fadeout');
                setTimeout(() => toast.remove(), 350);
            }
        }, 10000);
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
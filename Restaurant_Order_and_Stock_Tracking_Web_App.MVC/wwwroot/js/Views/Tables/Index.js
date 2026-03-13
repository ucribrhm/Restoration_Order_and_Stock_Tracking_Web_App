const reservations = JSON.parse(document.getElementById('reservationData').textContent);
const allTables = JSON.parse(document.getElementById('allTablesData').textContent);

// ── CSRF Token ────────────────────────────────────────────────
function getToken() {
    return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
}

// ── Fetch Yardımcısı ─────────────────────────────────────────
async function postJson(url, payload) {
    const res = await fetch(url, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': getToken()
        },
        body: JSON.stringify(payload)
    });

    const ct = res.headers.get('content-type') || '';
    if (res.status === 401 || (!ct.includes('application/json') && !res.ok)) {
        alert('Oturumunuz sona erdi. Giriş sayfasına yönlendiriliyorsunuz.');
        window.location.href = '/App/Auth/Login';
        throw new Error('Unauthorized');
    }

    return res.json();
}

// ── Modal ──────────────────────────────────────────────────────
function openModal(id) { document.getElementById(id).classList.add('open'); }
function closeModal(id) { document.getElementById(id).classList.remove('open'); }
document.querySelectorAll('.modal-overlay').forEach(o =>
    o.addEventListener('click', e => { if (e.target === o) o.classList.remove('open'); })
);

// ── Rezerve modal aç ─────────────────────────────────────────
function openReserveModal(tableId, tableName, maxCap) {
    document.getElementById('res-tableId').value = tableId;
    document.getElementById('res-maxCap').value = maxCap;
    document.getElementById('res-modal-title').textContent = tableName + ' — Rezervasyon';
    document.getElementById('res-guests').max = maxCap;
    const d = new Date(); d.setHours(d.getHours() + 1, 0, 0);
    document.getElementById('res-time').value =
        String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0');
    ['res-name', 'res-phone', 'res-guests', 'res-time'].forEach(id => {
        document.getElementById('err-' + id).style.display = 'none';
        document.getElementById(id).style.borderColor = '';
    });
    openModal('reserveModal');
}

// ── Masa Ekle — Fetch ─────────────────────────────────────────
async function submitCreateTable() {
    if (!validateForm()) return;

    const payload = {
        tableName: document.getElementById('add-name').value.trim(),
        tableCapacity: parseInt(document.getElementById('add-cap').value)
    };

    try {
        const data = await postJson('/App/Tables/Create', payload);
        if (data.success) {
            window.location.href = data.redirectUrl || window.location.href;
        } else {
            alert('Hata: ' + (data.message || 'Bilinmeyen hata'));
        }
    } catch (e) {
        if (e.message !== 'Unauthorized') alert('İstek gönderilemedi.');
    }
}

// ── Rezervasyon — Fetch ───────────────────────────────────────
async function submitReserve() {
    if (!validateReserveForm()) return;

    const payload = {
        tableId: parseInt(document.getElementById('res-tableId').value),
        reservationName: document.getElementById('res-name').value.trim(),
        reservationPhone: document.getElementById('res-phone').value.trim(),
        reservationGuestCount: parseInt(document.getElementById('res-guests').value),
        reservationTime: document.getElementById('res-time').value
    };

    try {
        const data = await postJson('/Tables/Reserve', payload);
        if (data.success) {
            window.location.href = data.redirectUrl || window.location.href;
        } else {
            alert('Hata: ' + (data.message || 'Bilinmeyen hata'));
        }
    } catch (e) {
        if (e.message !== 'Unauthorized') alert('İstek gönderilemedi.');
    }
}

// ── Rezervasyon İptal — Fetch ─────────────────────────────────
async function cancelReserve(tableId) {
    if (!confirm('Rezervasyon iptal edilsin mi?')) return;

    try {
        const data = await postJson('/Tables/CancelReserve', { tableId });
        if (data.success) { location.reload(); }
        else { alert('Hata: ' + (data.message || 'Bilinmeyen hata')); }
    } catch (e) {
        if (e.message !== 'Unauthorized') alert('İstek gönderilemedi.');
    }
}

// ── Masa Sil — Fetch ──────────────────────────────────────────
async function deleteTable(tableId, tableName) {
    if (!confirm(`'${tableName}' silinsin mi?`)) return;

    try {
        const data = await postJson('/Tables/Delete', { tableId });
        if (data.success) { location.reload(); }
        else { alert('Hata: ' + (data.message || 'Bilinmeyen hata')); }
    } catch (e) {
        if (e.message !== 'Unauthorized') alert('İstek gönderilemedi.');
    }
}

// ── Birleştirme modal ─────────────────────────────────────────
let mergeSourceId = null;
let mergeTargetId = null;

function openMergeModal(sourceTableId, sourceTableName) {
    mergeSourceId = sourceTableId;
    mergeTargetId = null;
    document.getElementById('merge-sourceTableId').value = sourceTableId;
    document.getElementById('merge-targetTableId').value = '';
    document.getElementById('merge-source-name').textContent = sourceTableName;
    document.getElementById('merge-sub').textContent =
        `${sourceTableName} adisyonunu hangi masayla birleştirmek istersiniz?`;
    document.getElementById('merge-submit-btn').disabled = true;

    const list = document.getElementById('mergeTargetList');
    list.innerHTML = '';

    const targets = allTables.filter(t => t.id !== sourceTableId);
    if (targets.length === 0) {
        list.innerHTML = '<div style="text-align:center;color:var(--text-muted);padding:20px">Başka masa bulunamadı.</div>';
    } else {
        targets.forEach(t => {
            const div = document.createElement('div');
            div.className = 'merge-option';
            div.dataset.targetId = t.id;
            const statusLabel = t.status === 1 ? '🔴 Dolu' : t.status === 2 ? '🔵 Rezerve' : '🟢 Boş';
            const infoText = t.status === 1
                ? `${t.itemCount} kalem · ₺${t.total.toFixed(2).replace('.', ',')} — adisyonlar birleşecek`
                : 'Adisyon bu masaya taşınacak';
            div.innerHTML = `
                <div style="font-size:20px">🪑</div>
                <div style="flex:1">
                    <div class="merge-option-name">${t.name}</div>
                    <div class="merge-option-info">${statusLabel} · ${infoText}</div>
                </div>
                ${t.status === 1 ? `<div class="merge-option-total">₺${t.total.toFixed(2).replace('.', ',')}</div>` : ''}
            `;
            div.addEventListener('click', () => selectMergeTarget(t.id, div));
            list.appendChild(div);
        });
    }

    openModal('mergeModal');
}

function selectMergeTarget(targetId, el) {
    document.querySelectorAll('.merge-option').forEach(o => o.classList.remove('selected'));
    el.classList.add('selected');
    mergeTargetId = targetId;
    document.getElementById('merge-targetTableId').value = targetId;
    document.getElementById('merge-submit-btn').disabled = false;
}

// ── Birleştir — Fetch ─────────────────────────────────────────
async function submitMerge() {
    if (!mergeTargetId) return;

    const src = allTables.find(t => t.id === mergeSourceId);
    const tgt = allTables.find(t => t.id === mergeTargetId);
    const msg = tgt.status === 1
        ? `${src.name} ve ${tgt.name} adisyonları birleştirilecek. Onaylıyor musunuz?`
        : `${src.name} adisyonu ${tgt.name} masasına taşınacak. Onaylıyor musunuz?`;

    if (!confirm(msg)) return;

    const submitBtn = document.getElementById('merge-submit-btn');
    submitBtn.disabled = true;
    submitBtn.textContent = '⏳ Birleştiriliyor...';

    try {
        const data = await postJson('/Tables/MergeOrder', {
            sourceTableId: mergeSourceId,
            targetTableId: mergeTargetId
        });

        if (data.success) {
            if (data.redirectUrl) { window.location.href = data.redirectUrl; }
            else { location.reload(); }
        } else {
            alert('Hata: ' + (data.message || 'Bilinmeyen hata'));
            submitBtn.disabled = false;
            submitBtn.textContent = '⇄ Birleştir';
        }
    } catch (e) {
        if (e.message !== 'Unauthorized') {
            alert('İstek gönderilemedi.');
            submitBtn.disabled = false;
            submitBtn.textContent = '⇄ Birleştir';
        }
    }
}

// ── Kalemleri genişlet / daralt ────────────────────────────────
function toggleItems(tableId, total, limit) {
    const container = document.getElementById('items-' + tableId);
    const btn = document.getElementById('more-' + tableId);
    const hidden = container.querySelectorAll('.order-items-hidden');
    const expanded = btn.dataset.expanded === 'true';

    if (expanded) {
        hidden.forEach(el => el.style.display = 'none');
        btn.textContent = `+${total - limit} kalem daha...`;
        btn.dataset.expanded = 'false';
    } else {
        container.querySelectorAll('[data-item-index]').forEach(el => { el.style.display = 'flex'; });
        btn.textContent = '▲ Daralt';
        btn.dataset.expanded = 'true';
    }
}

document.querySelectorAll('.order-items-hidden').forEach(el => { el.style.display = 'none'; });

// ── Rezervasyon detay ──────────────────────────────────────────
function showResDetail(tableId) {
    const r = reservations.find(x => x.id == tableId);
    if (!r) return;
    document.getElementById('resDetailContent').innerHTML = `
        <div class="res-detail-row"><div class="res-detail-icon">👤</div><div><div class="res-detail-label">İsim Soyisim</div><div class="res-detail-value">${r.resName}</div></div></div>
        <div class="res-detail-row"><div class="res-detail-icon">📞</div><div><div class="res-detail-label">Telefon</div><div class="res-detail-value">${r.resPhone}</div></div></div>
        <div class="res-detail-row"><div class="res-detail-icon">👥</div><div><div class="res-detail-label">Kişi Sayısı</div><div class="res-detail-value">${r.resGuests} kişi</div></div></div>
        <div class="res-detail-row"><div class="res-detail-icon">🕐</div><div><div class="res-detail-label">Rezervasyon Saati</div><div class="res-detail-value">${r.resTime}</div></div></div>
    `;
    openModal('resDetailModal');
}

// ── Validasyon ─────────────────────────────────────────────────
function validateForm() {
    let ok = true;
    const name = document.getElementById('add-name');
    const cap = document.getElementById('add-cap');
    const errN = document.getElementById('err-add-name');
    const errC = document.getElementById('err-add-cap');
    if (!name.value.trim()) { errN.style.display = 'block'; name.style.borderColor = '#ef4444'; ok = false; }
    else { errN.style.display = 'none'; name.style.borderColor = ''; }
    const v = parseInt(cap.value);
    if (isNaN(v) || v < 1 || v > 20) { errC.style.display = 'block'; cap.style.borderColor = '#ef4444'; ok = false; }
    else { errC.style.display = 'none'; cap.style.borderColor = ''; }
    return ok;
}

function validateReserveForm() {
    let ok = true;
    const maxCap = parseInt(document.getElementById('res-maxCap').value);
    const fields = [
        { id: 'res-name', err: 'err-res-name', check: v => v.trim().length > 0, msg: 'Ad soyad boş olamaz.' },
        { id: 'res-phone', err: 'err-res-phone', check: v => v.trim().length >= 10, msg: 'Geçerli telefon giriniz.' },
        { id: 'res-guests', err: 'err-res-guests', check: v => v >= 1 && v <= maxCap, msg: `1 ile ${maxCap} arasında olmalı.` },
        { id: 'res-time', err: 'err-res-time', check: v => v.length > 0, msg: 'Saat seçiniz.' },
    ];
    fields.forEach(f => {
        const el = document.getElementById(f.id);
        const err = document.getElementById(f.err);
        const val = f.id === 'res-guests' ? parseInt(el.value) : el.value;
        if (!f.check(val)) { err.textContent = f.msg; err.style.display = 'block'; el.style.borderColor = '#ef4444'; ok = false; }
        else { err.style.display = 'none'; el.style.borderColor = ''; }
    });
    return ok;
}

// ── Filtre ─────────────────────────────────────────────────────
document.querySelectorAll('.filter-btn').forEach(btn => {
    btn.addEventListener('click', () => {
        document.querySelectorAll('.filter-btn').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        const f = btn.dataset.filter;
        document.querySelectorAll('.table-card').forEach(c => {
            c.style.display = f === 'all' || c.dataset.status === f ? '' : 'none';
        });
    });
});

// ── Alert otomatik kaybol ──────────────────────────────────────
setTimeout(() => {
    document.querySelectorAll('.alert').forEach(a => {
        a.style.transition = 'opacity .5s'; a.style.opacity = '0';
        setTimeout(() => a.remove(), 500);
    });
}, 3000);

// ── Toast ──────────────────────────────────────────────────────
function showToast(id, type, icon, title, msg, autocloseMs) {
    if (document.getElementById(id)) return;
    const toast = document.createElement('div');
    toast.id = id; toast.className = `toast ${type}`;
    toast.innerHTML = `
        <div class="toast-icon">${icon}</div>
        <div class="toast-body">
            <div class="toast-title ${type}">${title}</div>
            <div class="toast-msg">${msg}</div>
        </div>
        <button class="toast-close" onclick="this.parentElement.remove()">×</button>`;
    document.getElementById('toastContainer').appendChild(toast);
    if (autocloseMs) setTimeout(() => toast.remove(), autocloseMs);
}

// ── Rezervasyon uyarı sistemi ──────────────────────────────────
function checkReservationWarnings() {
    const now = new Date();
    reservations.forEach(r => {
        const resTime = new Date(r.resTimeIso);
        const diffMin = Math.floor((resTime - now) / 60000);

        if (diffMin >= 0 && diffMin <= 30) {
            document.getElementById('toast-late-' + r.id)?.remove();
            showToast('toast-' + r.id, 'warning', '⚠️',
                `${r.name} — Rezervasyon Yaklaşıyor`,
                `<strong>${r.resName}</strong> (${r.resGuests} kişi) saat <strong>${r.resTime}</strong>'de bekleniyor. Kalan: ~${diffMin} dakika`,
                null);
        }
        if (diffMin < 0 && diffMin >= -30) {
            document.getElementById('toast-' + r.id)?.remove();
            showToast('toast-late-' + r.id, 'danger', '🚨',
                `${r.name} — Misafir Gelmedi Mi?`,
                `<strong>${r.resName}</strong>, saatinden <strong>${Math.abs(diffMin)} dk</strong> geçti. ${30 + diffMin} dk sonra oto-temizlenecek.`,
                null);
        }
        if (diffMin < -30) {
            document.getElementById('toast-' + r.id)?.remove();
            document.getElementById('toast-late-' + r.id)?.remove();
            if (!window._reloadScheduled) {
                window._reloadScheduled = true;
                showToast('toast-reload', 'info', 'ℹ️', 'Masa Durumu Güncellendi',
                    'Süresi dolan rezervasyon temizlendi. Sayfa yenileniyor...', 3500);
                setTimeout(() => location.reload(), 4000);
            }
        }
    });
}

checkReservationWarnings();
setInterval(checkReservationWarnings, 60000);


// ══════════════════════════════════════════════════════════════
// ── Garson SLA Timer ─────────────────────────────────────────
// ══════════════════════════════════════════════════════════════

function formatElapsed(totalSeconds) {
    if (totalSeconds < 60) return `${totalSeconds} sn önce`;
    if (totalSeconds < 3600) return `${Math.floor(totalSeconds / 60)} dk önce`;
    const h = Math.floor(totalSeconds / 3600);
    const m = Math.floor((totalSeconds % 3600) / 60);
    return `${h} sa ${m} dk önce`;
}

function tickSlaTimers() {
    const now = Date.now();
    document.querySelectorAll('.waiter-sla-timer[data-called-at]').forEach(span => {
        const calledAt = new Date(span.dataset.calledAt).getTime();
        if (isNaN(calledAt)) return;
        const elapsed = Math.floor((now - calledAt) / 1000);
        const elapsedMin = elapsed / 60;
        span.textContent = ` (${formatElapsed(elapsed)})`;
        const card = span.closest('.table-card');
        if (elapsedMin > 10) {
            span.style.color = '#ef4444';
            span.style.fontWeight = '700';
            if (card) card.classList.add('sla-violated');
        } else {
            span.style.color = '#fbbf24';
            span.style.fontWeight = '600';
            if (card) card.classList.remove('sla-violated');
        }
    });
}

tickSlaTimers();
setInterval(tickSlaTimers, 15000);


// ── Garson Çağrısını Onayla ───────────────────────────────────
async function dismissWaiter(tableName) {
    try {
        const data = await postJson('/Tables/DismissWaiter', { tableName });
        if (!data.success) console.warn('DismissWaiter başarısız:', data.message);
    } catch (err) {
        console.error('DismissWaiter hatası:', err);
    }
}


// ══════════════════════════════════════════════════════════════
// ── [SORUN-B ÇÖZÜM] Servis Et Fonksiyonu
//
// Index.cshtml'deki "Servis Et" butonu bu fonksiyonu çağırır:
//   onclick="serveOrder(@table.TableId, '...')"
//
// Endpoint: POST /App/Tables/ServeReadyItems
// Payload : { tableId }
//
// Ne yapar:
//   1. Masanın açık adisyonundaki TÜM Ready kalemleri Served yapar
//   2. Başarı sonrası masa kartından ready state'i temizler
//   3. SignalR üzerinden OrderServed event'i gelirse o da temizler (idempotent)
// ══════════════════════════════════════════════════════════════
async function serveOrder(tableId, tableName) {
    // Çift tıklamayı önle: butonu hemen devre dışı bırak
    const btn = document.querySelector(`.table-card[data-table-id="${tableId}"] .serve-order-btn`);
    if (btn) {
        btn.disabled = true;
        btn.textContent = '⏳ Servis ediliyor...';
    }

    try {
        const data = await postJson('/App/Tables/ServeReadyItems', { tableId });

        if (data.success) {
            // Sunucu başarılı — masa kartındaki ready state'i temizle
            _clearReadyState(tableId);

            showToast(
                `served-${tableId}-${Date.now()}`,
                'success',
                '🍽️',
                `${tableName} — Servis Tamamlandı`,
                data.message || 'Tüm hazır ürünler servis edildi.',
                5000
            );
        } else {
            // Hata: butonu geri aktif et
            if (btn) {
                btn.disabled = false;
                btn.textContent = '🍽️ Servis Et';
            }
            showToast(
                `serve-err-${tableId}`,
                'danger',
                '⚠️',
                'Servis Hatası',
                data.message || 'Bilinmeyen hata.',
                6000
            );
        }
    } catch (err) {
        if (err.message !== 'Unauthorized') {
            if (btn) {
                btn.disabled = false;
                btn.textContent = '🍽️ Servis Et';
            }
            console.error('[serveOrder] İstek hatası:', err);
        }
    }
}

// ── Masa kartındaki "hazır" görsel durumunu temizle ───────────
// Bu fonksiyon hem serveOrder hem de SignalR OrderServed handler tarafından
// çağrılır → idempotent tasarım (iki kez çağrılması sorun yaratmaz).
function _clearReadyState(tableId) {
    // [v3-FIX] tableId type normalize: SignalR'dan int gelir, dataset string → String()
    const id = String(tableId);
    const card = document.querySelector(`.table-card[data-table-id="${id}"]`);
    if (!card) return;

    // CSS sınıflarını kaldır
    card.classList.remove('order-ready', 'has-ready-items');

    // DB'den render edilmiş sabit rozeti kaldır (id="ready-badge-{tableId}")
    document.getElementById(`ready-badge-${tableId}`)?.remove();

    // SignalR ile dinamik eklenen per-item rozet'leri kaldır
    card.querySelectorAll('.ready-item-badge').forEach(b => b.remove());

    // "Servis Et" butonunu kaldır
    card.querySelector('.serve-order-btn')?.remove();
}


// ══════════════════════════════════════════════════════════════
// ── SignalR Bağlantısı — Bulletproof Edition
// ══════════════════════════════════════════════════════════════
(function initSignalR() {

    // ── [v3-FIX] TenantId'yi QueryString ile Hub'a ilet ─────────────────────
    // SORUN: withUrl('/hubs/restaurant') — QueryString yok.
    // Hub anonim/cookie'siz istemcileri Abort() ediyordu → sistem sağır.
    //
    // ÇÖZÜM: window.APP_TENANT_ID, _AppLayout.cshtml'de
    //   <meta name="ros-tenant-id"> tag'inden set edilir.
    // Garsonda: User.FindFirst("TenantId")?.Value (Claims)
    // KDS/QR'de:  meta tag'den okunur (QueryString ile Hub'a iletilir)
    //
    // WebSocket handshake'i bir HTTP isteğidir → QueryString korunur.
    // Tüm transport türlerinde (WS, LongPolling, SSE) güvenilir çalışır.
    const _tenantId = (window.APP_TENANT_ID || '').trim();
    const _hubUrl = _tenantId
        ? `/hubs/restaurant?tenantId=${encodeURIComponent(_tenantId)}`
        : '/hubs/restaurant'; // SysAdmin fallback (gruba eklenmez)

    const connection = new signalR.HubConnectionBuilder()
        .withUrl(_hubUrl)
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        // Information: gelen frame'ler konsolda görünür → debug için F12 → "[SignalR:Tables]"
        .configureLogging(signalR.LogLevel.Information)
        .build();

    // ── Bağlantı durum logları ────────────────────────────────
    connection.onreconnecting(err =>
        console.warn('[SignalR:Tables] Bağlantı koptu, yeniden bağlanıyor...', err));
    connection.onreconnected(cid =>
        console.info('[SignalR:Tables] Yeniden bağlandı. ConnectionId:', cid));
    connection.onclose(err =>
        console.error('[SignalR:Tables] Bağlantı kalıcı olarak kapandı.', err));

    // ── Payload case-insensitive okuma yardımcısı ─────────────
    // camelCase önce (SignalR System.Text.Json standardı),
    // PascalCase fallback (farklı serializer veya eski kod).
    function p(payload, key) {
        const camel = key.charAt(0).toLowerCase() + key.slice(1);
        const pascal = key.charAt(0).toUpperCase() + key.slice(1);
        const val = payload[camel] ?? payload[pascal];
        return (val === undefined || val === null) ? null : val;
    }

    // ── WaiterCalled ──────────────────────────────────────────
    // C# payload: { tableName, calledAtUtc }
    connection.off('WaiterCalled');
    connection.on('WaiterCalled', function (payload) {
        try {
            console.info('[SignalR:Tables] WaiterCalled ←', payload);

            const tableName = p(payload, 'tableName');
            const calledAtUtc = p(payload, 'calledAtUtc');

            if (!tableName) {
                console.warn('[SignalR:Tables] WaiterCalled — tableName boş, yoksayıldı.');
                return;
            }

            // CSS.escape: masa adında özel karakter ('Teras 1' gibi) varsa selector'ı kırmaz
            const card = document.querySelector(`.table-card[data-table-name="${CSS.escape(tableName)}"]`);
            if (!card) {
                console.warn('[SignalR:Tables] WaiterCalled — kart bulunamadı:', tableName);
                return;
            }

            card.classList.add('waiter-called');
            if (calledAtUtc) card.dataset.waiterCalledAt = calledAtUtc;

            if (!card.querySelector('.waiter-bell-badge')) {
                const badge = document.createElement('div');
                badge.className = 'waiter-bell-badge';
                badge.appendChild(document.createTextNode('🔔 Garson!'));

                if (calledAtUtc) {
                    const timerSpan = document.createElement('span');
                    timerSpan.className = 'waiter-sla-timer';
                    timerSpan.dataset.calledAt = calledAtUtc;
                    badge.appendChild(timerSpan);
                }

                card.prepend(badge);
                tickSlaTimers();
            }

            const actions = card.querySelector('.card-actions');
            if (actions && !actions.querySelector('.dismiss-waiter')) {
                const btn = document.createElement('button');
                btn.type = 'button';
                btn.className = 'card-btn dismiss-waiter';
                btn.textContent = '✅ İlgilenildi (Tamam)';
                btn.onclick = () => dismissWaiter(tableName);
                actions.appendChild(btn);
            }

        } catch (err) {
            console.error('[SignalR:Tables] WaiterCalled handler hatası:', err, payload);
        }
    });

    // ── WaiterDismissed ───────────────────────────────────────
    // C# payload: { tableName }
    connection.off('WaiterDismissed');
    connection.on('WaiterDismissed', function (payload) {
        try {
            console.info('[SignalR:Tables] WaiterDismissed ←', payload);

            const tableName = p(payload, 'tableName');
            if (!tableName) {
                console.warn('[SignalR:Tables] WaiterDismissed — tableName boş, yoksayıldı.');
                return;
            }

            const card = document.querySelector(`.table-card[data-table-name="${CSS.escape(tableName)}"]`);
            if (!card) {
                console.warn('[SignalR:Tables] WaiterDismissed — kart bulunamadı:', tableName);
                return;
            }

            card.classList.remove('waiter-called', 'sla-violated');
            card.removeAttribute('data-waiter-called-at');
            card.querySelector('.waiter-bell-badge')?.remove();
            card.querySelector('.dismiss-waiter')?.remove();

        } catch (err) {
            console.error('[SignalR:Tables] WaiterDismissed handler hatası:', err, payload);
        }
    });

    // ── OrderReadyForPickup ───────────────────────────────────
    // C# payload: { orderItemId, orderId, tableName, menuItemName, readyAt }
    // KitchenController.UpdateStatus, item Ready'ye geçtiğinde fırlatır.
    connection.off('OrderReadyForPickup');
    connection.on('OrderReadyForPickup', function (payload) {
        try {
            console.info('[SignalR:Tables] OrderReadyForPickup ←', payload);

            const orderItemId = p(payload, 'orderItemId');
            const tableName = p(payload, 'tableName');
            const menuItemName = p(payload, 'menuItemName') ?? 'Ürün';

            if (!orderItemId || !tableName) {
                console.warn('[SignalR:Tables] OrderReadyForPickup — eksik alan:', payload);
                return;
            }

            const card = document.querySelector(`.table-card[data-table-name="${CSS.escape(tableName)}"]`);
            if (!card) {
                // Garson başka bir ekrandaysa masa kartı olmayabilir → sadece toast
                console.warn('[SignalR:Tables] OrderReadyForPickup — kart bulunamadı:', tableName);
                showToast(`ready-notile-${orderItemId}`, 'success', '✅',
                    `${tableName} — Hazır!`,
                    `${menuItemName} servis için hazır (masa kartı bu ekranda yok)`,
                    8000);
                return;
            }

            // Aynı kalem için rozet zaten eklenmişse idempotent yoksay
            const badgeId = `ready-badge-item-${orderItemId}`;
            if (document.getElementById(badgeId)) {
                console.info('[SignalR:Tables] OrderReadyForPickup — rozet zaten var:', badgeId);
                return;
            }

            // Per-item hazır rozeti oluştur
            const badge = document.createElement('span');
            badge.id = badgeId;
            badge.className = 'ready-item-badge';
            badge.textContent = `✅ ${menuItemName} Hazır`;

            const itemsPreview = card.querySelector('.order-items-preview');
            if (itemsPreview) {
                itemsPreview.appendChild(badge);
            } else {
                card.appendChild(badge);
            }

            // Masa kartına "sipariş hazır" vurgusu ekle
            card.classList.add('order-ready', 'has-ready-items');

            // "Servis Et" butonu kart üzerinde değilse dinamik ekle
            const cardActions = card.querySelector('.card-actions');
            if (cardActions && !cardActions.querySelector('.serve-order-btn')) {
                const tableId = card.dataset.tableId;
                const tblName = card.dataset.tableName || tableName;
                const serveBtn = document.createElement('button');
                serveBtn.type = 'button';
                serveBtn.className = 'card-btn serve-order-btn';
                serveBtn.textContent = '🍽️ Servis Et';
                serveBtn.onclick = () => serveOrder(parseInt(tableId), tblName);
                cardActions.appendChild(serveBtn);
            }

            showToast(
                `ready-${orderItemId}`,
                'success', '✅',
                `${tableName} — Hazır!`,
                `${menuItemName} servis için hazır`,
                8000
            );

            // 90 saniye sonra per-item rozeti otomatik kaldır
            // (garson "Servis Et"e basmadıysa temizlik)
            setTimeout(() => {
                document.getElementById(badgeId)?.remove();
                if (document.body.contains(card) && !card.querySelector('.ready-item-badge')) {
                    card.classList.remove('has-ready-items');
                }
            }, 90000);

        } catch (err) {
            console.error('[SignalR:Tables] OrderReadyForPickup handler hatası:', err, payload);
        }
    });

    // ── OrderServed ───────────────────────────────────────────
    // C# payload: { orderId, tableId, tableName }
    // TablesController.ServeReadyItems başarılı olduğunda fırlatılır.
    // serveOrder() başarı sonrası _clearReadyState()'i zaten çağırır.
    // Bu handler aynı event'i BAŞKA bir sekmede/cihazda açık sayfa alırsa
    // oradaki UI'ı da günceller (idempotent).
    connection.off('OrderServed');
    connection.on('OrderServed', function (payload) {
        try {
            console.info('[SignalR:Tables] OrderServed ←', payload);

            const tableId = p(payload, 'tableId');
            const tableName = p(payload, 'tableName') ?? '';

            if (!tableId) {
                console.warn('[SignalR:Tables] OrderServed — tableId boş, yoksayıldı.');
                return;
            }

            // Masa kartındaki tüm hazır durumu temizle (idempotent)
            _clearReadyState(tableId);

            // Diğer sekmeler / cihazlar için toast
            showToast(
                `served-sig-${tableId}`,
                'success', '🍽️',
                `${tableName || 'Masa'} — Servis Edildi`,
                'Hazır ürünler servis edildi.',
                4000
            );

        } catch (err) {
            console.error('[SignalR:Tables] OrderServed handler hatası:', err, payload);
        }
    });

    // ── Bağlantıyı başlat ─────────────────────────────────────
    async function startConnection() {
        try {
            await connection.start();
            console.info('[SignalR:Tables] Bağlandı → /hubs/restaurant');
        } catch (err) {
            console.error('[SignalR:Tables] Bağlantı hatası, 5sn sonra tekrar:', err);
            setTimeout(startConnection, 5000);
        }
    }

    startConnection();

})();
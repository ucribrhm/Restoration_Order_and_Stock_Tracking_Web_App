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
        window.location.href = '/Auth/Login';
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
        const data = await postJson('/Tables/Create', payload);
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
// ── YENİ: Garson SLA Timer ───────────────────────────────────
// ══════════════════════════════════════════════════════════════

/**
 * Geçen saniyeyi "X sn önce / X dk önce / X sa X dk önce" formatına çevirir.
 */
function formatElapsed(totalSeconds) {
    if (totalSeconds < 60) return `${totalSeconds} sn önce`;
    if (totalSeconds < 3600) return `${Math.floor(totalSeconds / 60)} dk önce`;
    const h = Math.floor(totalSeconds / 3600);
    const m = Math.floor((totalSeconds % 3600) / 60);
    return `${h} sa ${m} dk önce`;
}

/**
 * Sayfadaki tüm .waiter-sla-timer span'larını tarar, içeriğini günceller.
 * 10 dakika = 600 sn üzerinde → kırmızı + sla-violated class
 */
function tickSlaTimers() {
    const now = Date.now();

    document.querySelectorAll('.waiter-sla-timer[data-called-at]').forEach(span => {
        const calledAt = new Date(span.dataset.calledAt).getTime();
        if (isNaN(calledAt)) return;

        const elapsed = Math.floor((now - calledAt) / 1000); // saniye cinsinden
        const elapsedMin = elapsed / 60;

        span.textContent = ` (${formatElapsed(elapsed)})`;

        const card = span.closest('.table-card');

        if (elapsedMin > 10) {
            // SLA İHLALİ — kırmızı
            span.style.color = '#ef4444';
            span.style.fontWeight = '700';
            if (card) card.classList.add('sla-violated');
        } else {
            // Normal uyarı — sarı
            span.style.color = '#fbbf24';
            span.style.fontWeight = '600';
            if (card) card.classList.remove('sla-violated');
        }
    });
}

// Sayfa yüklenir yüklenmez çalıştır, sonra her 15 saniyede bir güncelle
tickSlaTimers();
setInterval(tickSlaTimers, 15000);



// ── [MADDE-3] Sipariş Servis Et ─────────────────────────────────────────
// Tables/Index'teki "Servis Et" butonu çağırır → OrderService.UpdateItemStatusAsync
async function serveOrder(tableId, tableName) {
    try {
        // O masanın açık adisyonundaki Ready kalemleri bul ve Served yap
        const data = await postJson('/Tables/ServeReadyItems', { tableId });
        if (!data.success) {
            console.warn('ServeReadyItems başarısız:', data.message);
            showToast('toast-serve-err', 'danger', '⚠️', 'Hata', data.message || 'Servis işlemi başarısız', 4000);
        }
        // Başarı durumunda UI, SignalR'dan gelen "OrderServed" eventi ile güncellenir.
    } catch (err) {
        console.error('serveOrder hatası:', err);
    }
}

// ── Garson Çağrısını Onayla (Garson → DismissWaiter) ────────────────────
async function dismissWaiter(tableName) {
    try {
        const data = await postJson('/Tables/DismissWaiter', { tableName });
        if (!data.success) {
            console.warn('DismissWaiter başarısız:', data.message);
        }
        // Başarı durumunda UI, SignalR'dan gelen "WaiterDismissed" eventi ile güncellenir.
    } catch (err) {
        console.error('DismissWaiter hatası:', err);
    }
}


// ── SignalR Bağlantısı ───────────────────────────────────────────────────
(function initSignalR() {
    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/restaurant")
        .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    // ── WaiterCalled: Müşteri garson çağırdı ────────────────────────────
    // YENİ: payload artık calledAtUtc de içeriyor — SLA timer için kullanıyoruz
    connection.on("WaiterCalled", function (payload) {
        const card = document.querySelector(`.table-card[data-table-name="${payload.tableName}"]`);
        if (!card) return;

        card.classList.add('waiter-called');

        // data attribute'ü güncelle (sonraki tickSlaTimers çağrısı bunu okur)
        if (payload.calledAtUtc) {
            card.dataset.waiterCalledAt = payload.calledAtUtc;
        }

        if (!card.querySelector('.waiter-bell-badge')) {
            const badge = document.createElement('div');
            badge.className = 'waiter-bell-badge';

            // "🔔 Garson!" metni
            badge.appendChild(document.createTextNode('🔔 Garson!'));

            // SLA timer span
            if (payload.calledAtUtc) {
                const timerSpan = document.createElement('span');
                timerSpan.className = 'waiter-sla-timer';
                timerSpan.dataset.calledAt = payload.calledAtUtc;
                badge.appendChild(timerSpan);
            }

            card.prepend(badge);
            tickSlaTimers(); // yeni eklenen timer'ı anında hesapla
        }

        const actions = card.querySelector('.card-actions');
        if (actions && !actions.querySelector('.dismiss-waiter')) {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'card-btn dismiss-waiter';
            btn.textContent = '✅ İlgilenildi (Tamam)';
            btn.onclick = () => dismissWaiter(payload.tableName);
            actions.appendChild(btn);
        }
    });

    // ── WaiterDismissed: Garson ilgilendi ────────────────────────────────
    connection.on("WaiterDismissed", function (payload) {
        const card = document.querySelector(`.table-card[data-table-name="${payload.tableName}"]`);
        if (!card) return;

        card.classList.remove('waiter-called');
        card.classList.remove('sla-violated');
        card.removeAttribute('data-waiter-called-at');
        card.querySelector('.waiter-bell-badge')?.remove();
        card.querySelector('.dismiss-waiter')?.remove();
    });


    // ── [MADDE-3] OrderReady: Mutfak siparişi hazırladı → masa kartına rozet + buton ──
    connection.on("OrderReady", function (payload) {
        const card = document.querySelector(`.table-card[data-table-id="${payload.tableId}"]`);
        if (!card) return;

        card.classList.add('order-ready');

        // Rozet ekle (yoksa)
        if (!card.querySelector('.order-ready-badge')) {
            const badge = document.createElement('div');
            badge.className = 'order-ready-badge';
            badge.id = `ready-badge-${payload.tableId}`;
            badge.textContent = '🍽️ Sipariş Hazır!';
            card.prepend(badge);
        }

        // "Servis Et" butonu ekle (card-actions içine, yoksa)
        const actions = card.querySelector('.card-actions');
        if (actions && !actions.querySelector('.serve-order-btn')) {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'card-btn serve-order-btn';
            btn.setAttribute('data-table-id', payload.tableId);
            btn.innerHTML = '🍽️ Servis Et';
            btn.onclick = () => serveOrder(payload.tableId, payload.tableName);
            actions.insertBefore(btn, actions.firstChild);
        }

        // Toast: geçici bilgi
        showToast(
            'toast-ready-' + payload.tableId,
            'success', '🍽️',
            `${payload.tableName} — Sipariş Hazır`,
            `${payload.menuItemName} servis için hazır!`,
            8000
        );
    });

    // ── [MADDE-3] OrderServed: Garson servis etti → rozeti ve butonu kaldır ────────
    connection.on("OrderServed", function (payload) {
        const card = document.querySelector(`.table-card[data-table-id="${payload.tableId}"]`);
        if (!card) return;

        card.classList.remove('order-ready');
        card.querySelector(`#ready-badge-${payload.tableId}`)?.remove();
        card.querySelector('.serve-order-btn')?.remove();
    });

    // ── [MADDE-4] RemoveOrderCard: İptal/Birleştirme → masa kartını anında temizle ──
    // (Tables/Index sadece masa kartı gösterir; KDS kartı değil)
    // Bu event masa durumu değiştiğinde veya birleştirmede tetiklenir.
    // Tables/Index'teki masa kartı state'i zaten allTablesData'dan geliyor;
    // tam kart refresh için partial endpoint kullanıyoruz.
    connection.on("RemoveOrderCard", function (payload) {
        // Birleştirmede eski masanın durum güncellenmesi gerekiyor (zaten redirect var)
        // Tables/Index'de kart DOM'u etkilenmez — masa kartı tableId ile tanınır, orderId ile değil
    });

    connection.on("OrderUpdated", function (payload) {
        // Tables/Index: kart state'i DB'den; merkezi refresh tetiklenebilir
        // Şimdilik: birleştirme zaten location.reload yapıyor; iptal için masa kartı değişmez
    });
    // ── Bağlantıyı Başlat ────────────────────────────────────────────────
    async function startConnection() {
        try {
            await connection.start();
            console.log('[SignalR] Bağlandı → /hubs/restaurant');
        } catch (err) {
            console.error('[SignalR] Bağlantı hatası, 5sn sonra tekrar denenecek:', err);
            setTimeout(startConnection, 5000);
        }
    }

    startConnection();
})();
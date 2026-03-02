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

    // TableCreateDto ile eşleşen payload
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
        if (e.message !== 'Unauthorized')
            alert('İstek gönderilemedi.');
    }
}

// ── Rezervasyon — Fetch ───────────────────────────────────────
async function submitReserve() {
    if (!validateReserveForm()) return;

    const submitBtn = document.querySelector('#reserveModal .btn-primary');
    if (submitBtn) { submitBtn.disabled = true; submitBtn.textContent = '⏳ Kaydediliyor…'; }

    // TableReserveDto ile eşleşen payload
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
        } else if (data.isDuplicate) {
            // ── Çakışma uyarısı ─────────────────────────────────────────
            closeModal('reserveModal');
            showDuplicateReservationAlert(data.message);
        } else {
            alert('Hata: ' + (data.message || 'Bilinmeyen hata'));
        }
    } catch (e) {
        if (e.message !== 'Unauthorized')
            alert('İstek gönderilemedi.');
    } finally {
        if (submitBtn) { submitBtn.disabled = false; submitBtn.textContent = 'Rezerve Et'; }
    }
}

// ── Çakışma Uyarı Modalı ───────────────────────────────────────
function showDuplicateReservationAlert(message) {
    // Varsa öncekini kaldır
    document.getElementById('dupAlertOverlay')?.remove();

    const overlay = document.createElement('div');
    overlay.id = 'dupAlertOverlay';
    overlay.style.cssText = `
        position:fixed; inset:0; z-index:9999; background:rgba(0,0,0,.55);
        display:flex; align-items:center; justify-content:center;
        animation: fadeIn .2s ease;
    `;
    overlay.innerHTML = `
        <div style="
            background:#fff; border-radius:16px; padding:32px 28px; max-width:420px; width:90%;
            box-shadow:0 20px 60px rgba(0,0,0,.25); text-align:center; position:relative;
            animation: slideUp .25s ease;
        ">
            <div style="font-size:52px; margin-bottom:12px; line-height:1;">⚠️</div>
            <div style="font-size:17px; font-weight:700; color:#1e293b; margin-bottom:10px;">
                Çakışan Rezervasyon
            </div>
            <div style="font-size:14px; color:#475569; line-height:1.6; margin-bottom:24px;">
                ${message}
            </div>
            <button onclick="document.getElementById('dupAlertOverlay').remove()" style="
                background:linear-gradient(135deg,#ef4444,#dc2626); color:#fff; border:none;
                border-radius:10px; padding:10px 28px; font-size:14px; font-weight:600;
                cursor:pointer; transition:opacity .2s;
            " onmouseover="this.style.opacity='.85'" onmouseout="this.style.opacity='1'">
                Tamam, Anladım
            </button>
        </div>
        <style>
            @keyframes fadeIn  { from { opacity:0; } to { opacity:1; } }
            @keyframes slideUp { from { transform:translateY(30px); opacity:0; } to { transform:translateY(0); opacity:1; } }
        </style>
    `;
    // Overlay dışına tıklayınca kapat
    overlay.addEventListener('click', e => { if (e.target === overlay) overlay.remove(); });
    document.body.appendChild(overlay);
}

// ── Rezervasyon İptal — Fetch ─────────────────────────────────
async function cancelReserve(tableId) {
    if (!confirm('Rezervasyon iptal edilsin mi?')) return;

    // TableCancelReserveDto ile eşleşen payload
    try {
        const data = await postJson('/Tables/CancelReserve', { tableId });

        if (data.success) {
            location.reload();
        } else {
            alert('Hata: ' + (data.message || 'Bilinmeyen hata'));
        }
    } catch (e) {
        if (e.message !== 'Unauthorized')
            alert('İstek gönderilemedi.');
    }
}

// ── Masa Sil — Fetch ──────────────────────────────────────────
async function deleteTable(tableId, tableName) {
    if (!confirm(`'${tableName}' silinsin mi?`)) return;

    // TableDeleteDto ile eşleşen payload
    try {
        const data = await postJson('/Tables/Delete', { tableId });

        if (data.success) {
            location.reload();
        } else {
            alert('Hata: ' + (data.message || 'Bilinmeyen hata'));
        }
    } catch (e) {
        if (e.message !== 'Unauthorized')
            alert('İstek gönderilemedi.');
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

    // TableMergeOrderDto ile eşleşen payload
    try {
        const data = await postJson('/Tables/MergeOrder', {
            sourceTableId: mergeSourceId,
            targetTableId: mergeTargetId
        });

        if (data.success) {
            if (data.redirectUrl) {
                window.location.href = data.redirectUrl;
            } else {
                location.reload();
            }
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
        container.querySelectorAll('[data-item-index]').forEach(el => {
            el.style.display = 'flex';
        });
        btn.textContent = '▲ Daralt';
        btn.dataset.expanded = 'true';
    }
}

document.querySelectorAll('.order-items-hidden').forEach(el => {
    el.style.display = 'none';
});

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

// ── Validasyon (UI için korundu) ───────────────────────────────
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
    connection.on("WaiterCalled", function (payload) {
        // data-table-name attribute'ü üzerinden kartı bul
        console.log("Sunucuya giden masa adı: ", payload.tableName);
        const card = document.querySelector(`.table-card[data-table-name="${payload.tableName}"]`);
        if (!card) return;

        card.classList.add('waiter-called');

        if (!card.querySelector('.waiter-bell-badge')) {
            const badge = document.createElement('div');
            badge.className = 'waiter-bell-badge';
            badge.textContent = '🔔 Garson!';
            card.prepend(badge);
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
        card.querySelector('.waiter-bell-badge')?.remove();
        card.querySelector('.dismiss-waiter')?.remove();
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

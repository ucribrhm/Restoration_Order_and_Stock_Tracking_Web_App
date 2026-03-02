function openModal(id) { document.getElementById(id).classList.add('open'); }
function closeModal(id) { document.getElementById(id).classList.remove('open'); }

document.querySelectorAll('.modal-overlay').forEach(overlay =>
    overlay.addEventListener('click', e => {
        if (e.target === overlay) overlay.classList.remove('open');
    })
);

function showToast(msg, type = 'success') {
    const c = document.getElementById('toastContainer');
    const t = document.createElement('div');
    t.className = 'toast';
    t.innerHTML = `<span>${type === 'success' ? '✅' : '❌'}</span><span>${msg}</span>`;
    c.appendChild(t);
    setTimeout(() => t.remove(), 3500);
}

function getToken() {
    return document.querySelector('input[name="__RequestVerificationToken"]').value;
}

async function postJson(url, payload) {
    const res = await fetch(url, {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'RequestVerificationToken': getToken()
        },
        body: JSON.stringify(payload)
    });
    return res.json();
}

// ══════════════════════════════════════════════════════════════
//  StockAccordion
// ══════════════════════════════════════════════════════════════
const StockAccordion = {
    toggle(headerEl) { headerEl.closest('.acc-block').classList.toggle('open'); }
};

// ══════════════════════════════════════════════════════════════
//  StockSparkline
// ══════════════════════════════════════════════════════════════
const StockSparkline = {
    drawAll() {
        document.querySelectorAll('canvas.sparkline').forEach(c => {
            let vals;
            try { vals = JSON.parse(c.dataset.vals || '[]'); } catch { vals = []; }
            this.draw(c, vals);
        });
    },
    draw(canvas, values) {
        const ctx = canvas.getContext('2d');
        const w = canvas.width, h = canvas.height, pad = 3;
        ctx.clearRect(0, 0, w, h);
        if (!values || values.length < 2) {
            ctx.strokeStyle = 'rgba(148,163,184,.4)';
            ctx.lineWidth = 1.5;
            ctx.beginPath(); ctx.moveTo(pad, h / 2); ctx.lineTo(w - pad, h / 2); ctx.stroke();
            return;
        }
        const mn = Math.min(...values), mx = Math.max(...values), rng = mx - mn || 1;
        const pts = values.map((v, i) => ({
            x: pad + (i / (values.length - 1)) * (w - pad * 2),
            y: h - pad - ((v - mn) / rng) * (h - pad * 2)
        }));
        const grad = ctx.createLinearGradient(0, 0, 0, h);
        grad.addColorStop(0, 'rgba(99,102,241,.35)');
        grad.addColorStop(1, 'rgba(99,102,241,.02)');
        ctx.beginPath();
        pts.forEach((p, i) => i === 0 ? ctx.moveTo(p.x, p.y) : ctx.lineTo(p.x, p.y));
        ctx.lineTo(pts[pts.length - 1].x, h); ctx.lineTo(pts[0].x, h);
        ctx.closePath(); ctx.fillStyle = grad; ctx.fill();
        ctx.beginPath();
        pts.forEach((p, i) => i === 0 ? ctx.moveTo(p.x, p.y) : ctx.lineTo(p.x, p.y));
        ctx.strokeStyle = '#6366f1'; ctx.lineWidth = 1.8; ctx.lineJoin = 'round'; ctx.stroke();
    }
};

// ══════════════════════════════════════════════════════════════
//  StockFilter
// ══════════════════════════════════════════════════════════════
const StockFilter = {
    apply() {
        const q = document.getElementById('searchInput').value.trim().toLowerCase();
        const cat = document.getElementById('catFilter').value;
        const st = document.getElementById('statusFilter').value;
        document.querySelectorAll('.stock-row').forEach(row => {
            const matchQ = !q || row.dataset.name.includes(q) || row.dataset.sku.includes(q);
            const matchCat = !cat || row.dataset.cat === cat;
            const matchSt = !st || row.dataset.status === st;
            row.classList.toggle('row-hidden', !(matchQ && matchCat && matchSt));
        });
        document.querySelectorAll('.acc-block').forEach(block => {
            const visible = block.querySelectorAll('.stock-row:not(.row-hidden)').length;
            block.style.display = visible === 0 ? 'none' : '';
        });
    }
};

// ══════════════════════════════════════════════════════════════
//  StockCsv
// ══════════════════════════════════════════════════════════════
const StockCsv = {
    export() {
        const headers = ['Ürün Adı', 'SKU', 'Kategori', 'Güncel Stok', 'Uyarı Eşiği', 'Durum', 'Son Güncelleme'];
        const rows = [headers];
        document.querySelectorAll('.stock-row:not(.row-hidden)').forEach(row => {
            const id = row.dataset.id;
            const nm = row.querySelector('td:first-child div:first-child')?.textContent.trim() ?? '';
            const sku = row.querySelector('td:first-child div:last-child')?.textContent.trim() ?? '';
            const cat = row.dataset.cat;
            const qty = document.getElementById(`qty-${id}`)?.textContent.trim() ?? '';
            const thr = document.getElementById(`thresh-${id}`)?.textContent.trim() ?? '';
            const st = row.dataset.status;
            const upd = row.querySelectorAll('td')[5]?.textContent.trim() ?? '';
            rows.push([nm, sku, cat, qty, thr, st, upd]);
        });
        const csv = rows.map(r => r.map(c => `"${String(c).replace(/"/g, '""')}"`).join(',')).join('\r\n');
        const blob = new Blob(['\uFEFF' + csv], { type: 'text/csv;charset=utf-8;' });
        const url = URL.createObjectURL(blob);
        const a = Object.assign(document.createElement('a'), {
            href: url, download: `stok_raporu_${new Date().toISOString().slice(0, 10)}.csv`
        });
        a.click();
        URL.revokeObjectURL(url);
    }
};

// ══════════════════════════════════════════════════════════════
//  StockUpdateModal — 3 sekme: Direkt | Hareket | 🔥 Fire
// ══════════════════════════════════════════════════════════════
const StockUpdateModal = {
    currentId: null,
    direction: 'in',
    activeTab: 'direct',   // 'direct' | 'movement' | 'fire'

    open(id, name, sku, stock, threshold) {
        this.currentId = id;
        this.direction = 'in';
        this.activeTab = 'direct';

        document.getElementById('upd_id').value = id;
        document.getElementById('upd_title').textContent = name;
        document.getElementById('upd_sku').textContent = sku;
        document.getElementById('upd_direct').value = stock;
        //document.getElementById('upd_threshold').value = threshold > 0 ? threshold : '';
        document.getElementById('upd_qty').value = '';
        document.getElementById('upd_note').value = '';
        document.getElementById('upd_fire_qty').value = '';
        document.getElementById('upd_fire_note').value = '';

        this.switchTab('direct', document.querySelectorAll('.tab-btn')[0]);
        this.setDirection('in');
        openModal('updateModal');
    },

    switchTab(tab, btnEl) {
        this.activeTab = tab;
        document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
        document.querySelectorAll('.tab-panel').forEach(p => p.classList.remove('active'));
        if (btnEl) btnEl.classList.add('active');
        const panel = document.getElementById(`tab-${tab}`);
        if (panel) panel.classList.add('active');
    },

    setDirection(dir) {
        this.direction = dir;
        document.getElementById('dir-in').className = dir === 'in' ? 'dir-btn active-in' : 'dir-btn';
        document.getElementById('dir-out').className = dir === 'out' ? 'dir-btn active-out' : 'dir-btn';
    }
};

// ── updateForm submit ──────────────────────────────────────────
document.getElementById('updateForm').addEventListener('submit', async e => {
    e.preventDefault();
    const btn = e.submitter ?? document.querySelector('#updateForm button[type="submit"]');
    btn.disabled = true;

    const activeTab = StockUpdateModal.activeTab;
    const threshold = document.getElementById('upd_threshold').value;

    const payload = {
        menuItemId: StockUpdateModal.currentId,
        updateMode: activeTab,   // 'direct' | 'movement' | 'fire'
        alertThreshold: threshold !== '' ? parseInt(threshold) : null,
        criticalThreshold: null
    };

    if (activeTab === 'direct') {
        const v = document.getElementById('upd_direct').value;
        if (v === '' || parseInt(v) < 0) {
            showToast('Geçerli bir stok değeri giriniz.', 'error');
            btn.disabled = false; return;
        }
        payload.newStockValue = parseInt(v);

    } else if (activeTab === 'movement') {
        const qty = document.getElementById('upd_qty').value;
        const note = document.getElementById('upd_note').value.trim();
        if (!qty || parseInt(qty) <= 0) {
            showToast('Geçerli bir miktar giriniz.', 'error');
            btn.disabled = false; return;
        }
        if (!note) {
            showToast('Hareket bazlı işlemde açıklama zorunludur.', 'error');
            btn.disabled = false; return;
        }
        payload.movementDirection = StockUpdateModal.direction;
        payload.movementQuantity = parseInt(qty);
        payload.note = note;

    } else if (activeTab === 'fire') {
        // 🔥 Fire / Zayi Çıkışı — ayrı sekme, SourceType="StokKaynaklı"
        const qty = document.getElementById('upd_fire_qty').value;
        const note = document.getElementById('upd_fire_note').value.trim();
        if (!qty || parseInt(qty) <= 0) {
            showToast('Fire miktarını giriniz.', 'error');
            btn.disabled = false; return;
        }
        if (!note) {
            showToast('Fire nedenini açıklamak zorunludur.', 'error');
            btn.disabled = false; return;
        }
        payload.movementQuantity = parseInt(qty);
        payload.note = note;
        // updateMode='fire' → Controller StokKaynaklı kaydeder
    }

    try {
        const data = await postJson('/Stock/UpdateStock', payload);
        btn.disabled = false;

        if (data.success) {
            const id = StockUpdateModal.currentId;
            const qtyEl = document.getElementById(`qty-${id}`);
            const statusEl = document.getElementById(`status-${id}`);
            const threshEl = document.getElementById(`thresh-${id}`);
            const row = document.querySelector(`.stock-row[data-id="${id}"]`);

            if (qtyEl) qtyEl.textContent = data.newStock;
            if (threshEl && threshold !== '') threshEl.textContent = parseInt(threshold);
            if (statusEl) {
                statusEl.className = `pill ${data.statusPill}`;
                statusEl.textContent = data.statusLabel;
            }
            if (row) { row.dataset.status = data.status; row.dataset.qty = data.newStock; }

            closeModal('updateModal');
            showToast(data.message, 'success');
        } else {
            showToast(data.message, 'error');
        }
    } catch {
        btn.disabled = false;
        showToast('Sunucu bağlantı hatası.', 'error');
    }
});

// ══════════════════════════════════════════════════════════════
//  StockHistoryModal
// ══════════════════════════════════════════════════════════════
const StockHistoryModal = {
    async open(id) {
        document.getElementById('hist_title').textContent = 'Stok Geçmişi';
        document.getElementById('hist_sku').textContent = '…';
        document.getElementById('hist_body').innerHTML =
            '<div style="text-align:center;padding:2rem;color:var(--text-muted);">Yükleniyor…</div>';
        openModal('historyModal');

        try {
            const res = await fetch(`/Stock/GetHistory/${id}`);
            const data = await res.json();

            if (!data.success) {
                document.getElementById('hist_body').innerHTML =
                    '<div style="text-align:center;padding:2rem;color:var(--text-muted);">Veri alınamadı.</div>';
                return;
            }

            document.getElementById('hist_title').textContent = data.itemName;
            document.getElementById('hist_sku').textContent = data.sku;

            if (!data.logs || data.logs.length === 0) {
                document.getElementById('hist_body').innerHTML =
                    '<div style="text-align:center;padding:2rem;color:var(--text-muted);">📭 Bu ürün için henüz stok hareketi kaydı bulunmuyor.</div>';
                return;
            }

            const typeClass = t => t === 'Giriş' ? 'style="color:#34d399;font-weight:600;"'
                : t === 'Çıkış' ? 'style="color:#f87171;font-weight:600;"'
                    : 'style="color:#818cf8;font-weight:600;"';

            const changeHtml = q => q > 0
                ? `<span style="color:#34d399;">+${q}</span>`
                : `<span style="color:#f87171;">${q}</span>`;

            // SourceType badge
            const sourceBadge = s => {
                if (s === 'SiparişKaynaklı') return '<span style="font-size:.72rem;background:rgba(251,191,36,.15);color:#fbbf24;padding:2px 7px;border-radius:20px;border:1px solid rgba(251,191,36,.3);">🧾 Sipariş</span>';
                if (s === 'StokKaynaklı') return '<span style="font-size:.72rem;background:rgba(239,68,68,.15);color:#f87171;padding:2px 7px;border-radius:20px;border:1px solid rgba(239,68,68,.3);">🔥 Fire</span>';
                return '';
            };

            document.getElementById('hist_body').innerHTML = `
                <div style="overflow-x:auto; padding:0 .25rem .5rem;">
                    <table class="ros-table">
                        <thead><tr>
                            <th>Tarih</th>
                            <th>Tür</th>
                            <th>Kaynak</th>
                            <th>Değişim</th>
                            <th>Önceki → Sonraki</th>
                            <th>Not</th>
                        </tr></thead>
                        <tbody>
                            ${data.logs.map(l => `
                            <tr>
                                <td style="white-space:nowrap;opacity:.7;">${l.createdAt}</td>
                                <td><span ${typeClass(l.movementType)}>${l.movementType}</span></td>
                                <td>${sourceBadge(l.sourceType)}</td>
                                <td>${changeHtml(l.quantityChange)}</td>
                                <td>
                                    <span style="opacity:.5;">${l.previousStock}</span>
                                    <span style="opacity:.35;margin:0 .3rem;">→</span>
                                    <strong>${l.newStock}</strong>
                                </td>
                                <td style="opacity:.65;font-size:.82rem;">${l.note}</td>
                            </tr>`).join('')}
                        </tbody>
                    </table>
                </div>`;
        } catch {
            document.getElementById('hist_body').innerHTML =
                '<div style="text-align:center;padding:2rem;color:var(--text-muted);">⚠️ Bağlantı hatası oluştu.</div>';
        }
    }
};

// ══════════════════════════════════════════════════════════════
//  Track Toggle
// ══════════════════════════════════════════════════════════════
document.querySelectorAll('.track-toggle').forEach(chk => {
    chk.addEventListener('change', async function () {
        const id = this.dataset.id;
        const checked = this.checked;
        const payload = { menuItemId: parseInt(id), trackStock: checked };
        try {
            const data = await postJson('/Stock/ToggleTrack', payload);
            if (data.success) {
                const statusEl = document.getElementById(`status-${id}`);
                const row = document.querySelector(`.stock-row[data-id="${id}"]`);
                if (statusEl) { statusEl.className = `pill ${data.statusPill}`; statusEl.textContent = data.statusLabel; }
                if (row) row.dataset.status = data.status;
                showToast(data.message, 'success');
            } else {
                this.checked = !checked;
                showToast(data.message, 'error');
            }
        } catch {
            this.checked = !checked;
            showToast('Sunucu bağlantı hatası.', 'error');
        }
    });
});

// ══════════════════════════════════════════════════════════════
//  Init
// ══════════════════════════════════════════════════════════════
document.addEventListener('DOMContentLoaded', () => {
    StockSparkline.drawAll();
});
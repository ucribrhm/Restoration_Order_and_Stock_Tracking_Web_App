document.addEventListener("DOMContentLoaded", () => {

    // ── 1. JSON Data Island'dan C# Verilerini Oku ──
    const configEl = document.getElementById('orderConfigData');
    let config = { orderTotal: 0, alreadyPaid: 0, orderId: 0 };

    if (configEl) {
        config = JSON.parse(configEl.textContent);
    }

    const orderTotal = parseFloat(config.orderTotal);
    const alreadyPaid = parseFloat(config.alreadyPaid);
    const orderId = parseInt(config.orderId);
    let currentMethod = 'cash';

    // ── CSRF Token ──────────────────────────────────────────────
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
            window.location.href = window.APP_URLS?.authLogin ?? '/App/Auth/Login';
            throw new Error('Unauthorized');
        }

        return res.json();
    }

    // ── helpers ──────────────────────────────────────────────────
    function parseLD(str) {
        if (!str) return 0;
        const v = parseFloat(str.trim().replace(/\./g, '').replace(',', '.'));
        return isNaN(v) ? 0 : v;
    }
    function fmt(n) { return '₺' + n.toFixed(2).replace('.', ','); }

    // ── modal ────────────────────────────────────────────────────
    window.openModal = function (id) { document.getElementById(id).classList.add('open'); };
    window.closeModal = function (id) { document.getElementById(id).classList.remove('open'); };

    document.querySelectorAll('.modal-overlay').forEach(o => {
        o.addEventListener('click', e => { if (e.target === o) o.classList.remove('open'); });
    });

    // ═══════════════════════════════════════════════════════════
    // DURUM GÜNCELLEME — UpdateItemStatus
    // ═══════════════════════════════════════════════════════════
    window.updateItemStatus = async function (orderItemId, newStatus) {
        try {
            const data = await postJson(window.APP_URLS.ordersUpdateItemStatus, {
                orderItemId,
                newStatus,
                orderId
            });

            if (data.success) { location.reload(); }
            else { alert('Hata: ' + (data.message || 'Bilinmeyen hata')); }
        } catch (e) {
            if (e.message !== 'Unauthorized') alert('İstek gönderilemedi.');
        }
    };

    // ═══════════════════════════════════════════════════════════
    // AIM — Çoklu Ürün Ekleme
    // ═══════════════════════════════════════════════════════════
    let aimPrice = 0;
    let aimQty = 1;
    let aimCurId = null;
    let aimCurName = '';
    let aimCatActive = 'all';
    let aimBasket = [];
    let aimMaxStock = 0;
    let aimTrackStock = false;

    window.aimPick = function (id) {
        const row = document.getElementById('arow-' + id);
        if (!row) return;
        if (row.classList.contains('aim-row-disabled')) return;

        document.querySelectorAll('.aim-row.picked').forEach(r => r.classList.remove('picked'));
        row.classList.add('picked');

        aimCurId = parseInt(id);
        aimCurName = row.dataset.name;
        aimPrice = parseFloat(row.dataset.price);
        aimQty = 1;

        aimTrackStock = (row.dataset.track ?? 'false') === 'true';
        const rawStock = parseInt(row.dataset.stock ?? '', 10);
        aimMaxStock = (aimTrackStock && Number.isInteger(rawStock) && rawStock >= 0)
            ? rawStock
            : Infinity;

        document.getElementById('aimSelName').textContent = aimCurName;
        document.getElementById('aimSelUnit').textContent = fmt(aimPrice) + ' / adet';
        document.getElementById('aimNoteInp').value = '';
        aimRefresh();

        document.getElementById('aimPh').style.display = 'none';
        document.getElementById('aimForm').style.display = 'flex';
        document.getElementById('aimForm').style.flexDirection = 'column';
    };

    window.aimDelta = function (d) {
        const newQty = aimQty + d;

        if (aimTrackStock && Number.isFinite(aimMaxStock) && newQty > aimMaxStock) {
            showAimToast(`Ürün stoğu = ${aimMaxStock} kadar ekleme yapabilirsiniz.`, 'warning');
            aimQty = aimMaxStock;
            aimRefresh();
            return;
        }

        const upperBound = (aimTrackStock && Number.isFinite(aimMaxStock)) ? aimMaxStock : 99;
        aimQty = Math.max(1, Math.min(upperBound, newQty));
        aimRefresh();
    };

    function aimRefresh() {
        document.getElementById('aimQtyNum').textContent = aimQty;
        document.getElementById('aimTotal').textContent = fmt(aimPrice * aimQty);
        document.getElementById('aimAddBtn').textContent =
            '+ Sepete Ekle (' + aimQty + ' adet — ' + fmt(aimPrice * aimQty) + ')';

        let stockLbl = document.getElementById('aimStockHint');
        if (aimTrackStock && Number.isFinite(aimMaxStock)) {
            if (!stockLbl) {
                stockLbl = document.createElement('div');
                stockLbl.id = 'aimStockHint';
                stockLbl.style.cssText = 'font-size:.8rem;margin-top:4px;';
                document.getElementById('aimQtyNum').closest('.aim-qty-box').appendChild(stockLbl);
            }
            const remaining = aimMaxStock - aimQty;
            stockLbl.style.color = remaining <= 2 ? '#f87171' : 'var(--text-muted)';
            stockLbl.textContent = `Stok: ${aimMaxStock} adet (kalan ${remaining})`;
        } else if (stockLbl) {
            stockLbl.remove();
        }
    }

    window.aimAddToBasket = function () {
        if (!aimCurId) return;
        const note = (document.getElementById('aimNoteInp').value || '').trim();

        if (aimTrackStock && Number.isFinite(aimMaxStock)) {
            const alreadyInBasket = aimBasket
                .filter(i => i.id === aimCurId)
                .reduce((s, i) => s + i.qty, 0);
            const totalWanted = alreadyInBasket + aimQty;
            if (totalWanted > aimMaxStock) {
                const canAdd = Math.max(0, aimMaxStock - alreadyInBasket);
                showAimToast(
                    canAdd > 0
                        ? `Ürün stoğu = ${aimMaxStock} kadar ekleme yapabilirsiniz. (Sepette zaten ${alreadyInBasket} adet var)`
                        : `Bu üründen sepete maksimum ${aimMaxStock} adet ekleyebilirsiniz.`,
                    'warning'
                );
                return;
            }
        }

        const existing = aimBasket.find(i => i.id === aimCurId && i.note === note);
        if (existing) {
            const cap = (aimTrackStock && Number.isFinite(aimMaxStock)) ? aimMaxStock : 999;
            existing.qty = Math.min(cap, existing.qty + aimQty);
        } else {
            aimBasket.push({
                id: aimCurId, name: aimCurName, price: aimPrice, qty: aimQty, note,
                maxStock: (aimTrackStock && Number.isFinite(aimMaxStock)) ? aimMaxStock : Infinity
            });
        }

        aimQty = 1;
        document.getElementById('aimNoteInp').value = '';
        aimRefresh();

        document.getElementById('aimPh').style.display = '';
        document.getElementById('aimForm').style.display = 'none';
        document.querySelectorAll('.aim-row.picked').forEach(r => r.classList.remove('picked'));
        aimCurId = null;

        aimRenderBasket();
        document.getElementById('aimBasketWrap').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    };

    function aimRenderBasket() {
        const list = document.getElementById('aimBasketList');
        const empty = document.getElementById('aimBasketEmpty');
        const countEl = document.getElementById('aimBasketCount');
        const totalEl = document.getElementById('aimBasketTotalVal');
        const sendBtn = document.getElementById('aimSendBtn');

        Array.from(list.children).forEach(child => { if (child !== empty) child.remove(); });

        if (!aimBasket.length) {
            empty.style.display = '';
            countEl.textContent = '0';
            totalEl.textContent = '₺0,00';
            sendBtn.disabled = true;
            sendBtn.textContent = '✓ Tümünü Adisyona Gönder';
            return;
        }

        empty.style.display = 'none';
        let total = 0;

        aimBasket.forEach((item, idx) => {
            total += item.price * item.qty;
            const div = document.createElement('div');
            div.className = 'aim-basket-item';
            div.innerHTML =
                `<span class="aim-bi-name" title="${item.name}">${item.name}</span>` +
                `<span class="aim-bi-qty">×${item.qty}</span>` +
                `<span class="aim-bi-price">${fmt(item.price * item.qty)}</span>` +
                `<button type="button" class="aim-bi-del" onclick="aimRemoveFromBasket(${idx})" title="Kaldır">×</button>`;
            if (item.note) {
                const noteEl = document.createElement('span');
                noteEl.className = 'aim-bi-note';
                noteEl.textContent = '📝 ' + item.note;
                div.appendChild(noteEl);
            }
            list.appendChild(div);
        });

        const totalItems = aimBasket.reduce((s, i) => s + i.qty, 0);
        countEl.textContent = totalItems;
        totalEl.textContent = fmt(total);
        sendBtn.disabled = false;
        sendBtn.textContent = '✓ ' + totalItems + ' Ürünü Adisyona Gönder';
    }

    window.aimRemoveFromBasket = function (idx) {
        aimBasket.splice(idx, 1);
        aimRenderBasket();
    };

    window.aimClearBasket = function () {
        aimBasket = [];
        aimRenderBasket();
    };

    function showAimToast(message, type = 'success') {
        const container = document.getElementById('detailToastContainer');
        if (!container) return;

        const icons = { success: '✅', error: '❌', warning: '⚠️' };
        const toast = document.createElement('div');
        toast.className = `aim-toast aim-toast-${type}`;
        toast.innerHTML = `<span>${icons[type] || '⚠️'}</span><span>${message}</span>`;
        container.appendChild(toast);

        requestAnimationFrame(() => toast.classList.add('aim-toast-show'));
        setTimeout(() => {
            toast.classList.remove('aim-toast-show');
            setTimeout(() => toast.remove(), 400);
        }, 3500);
    }

    function showStockError(msg) {
        const prev = document.getElementById('aim-stock-error');
        if (prev) prev.remove();

        const banner = document.createElement('div');
        banner.id = 'aim-stock-error';
        banner.style.cssText = [
            'background:rgba(239,68,68,.12)',
            'border:1px solid rgba(239,68,68,.4)',
            'border-radius:8px',
            'color:#f87171',
            'padding:10px 14px',
            'margin:10px 0 4px',
            'font-size:.88rem',
            'line-height:1.45',
        ].join(';');
        banner.textContent = '⚠️ ' + msg;

        const basket = document.getElementById('aimBasketList') ?? document.getElementById('aimSendBtn')?.parentElement;
        if (basket) basket.insertAdjacentElement('beforebegin', banner);
        else document.getElementById('addItemModal')?.querySelector('.modal-body')?.prepend(banner);

        setTimeout(() => banner.remove(), 6000);
    }

    window.aimSendAll = async function () {
        if (!aimBasket.length) return;

        const btn = document.getElementById('aimSendBtn');
        btn.disabled = true;
        btn.textContent = '⏳ Gönderiliyor...';

        const payload = {
            orderId,
            items: aimBasket.map(i => ({
                menuItemId: i.id,
                quantity: i.qty,
                note: i.note || null
            }))
        };

        try {
            const data = await postJson(window.APP_URLS.ordersAddItemBulk, payload);

            if (data.success) {
                window.closeModal('addItemModal');
                location.reload();
            } else {
                btn.disabled = false;
                btn.textContent = '✓ Tümünü Adisyona Gönder';
                showAimToast(data.message || 'Bilinmeyen hata', 'warning');
            }
        } catch (e) {
            if (e.message !== 'Unauthorized') {
                btn.disabled = false;
                btn.textContent = '✓ Tümünü Adisyona Gönder';
                alert('İstek gönderilemedi. Lütfen sayfayı yenileyip tekrar deneyin.');
            }
        }
    };

    window.aimCat = function (catKey, btn) {
        aimCatActive = catKey;
        document.querySelectorAll('.aim-ctab').forEach(b => b.classList.remove('on'));
        if (btn) btn.classList.add('on');
        document.getElementById('aimQ').value = '';
        document.getElementById('aimLbl').classList.remove('on');
        document.querySelectorAll('.aim-row').forEach(r => r.style.display = '');
        document.querySelectorAll('.aim-cat-blk').forEach(b => {
            b.style.display = (catKey === 'all' || b.id === catKey) ? '' : 'none';
        });
        document.getElementById('aimNoRes').classList.remove('on');
    };

    window.aimSearch = function (q) {
        q = (q || '').toLowerCase().trim();
        const lbl = document.getElementById('aimLbl');
        if (!q) {
            window.aimCat(aimCatActive, document.querySelector('.aim-ctab[data-c="' + aimCatActive + '"]'));
            return;
        }
        document.querySelectorAll('.aim-ctab').forEach(b => b.classList.remove('on'));
        document.querySelectorAll('.aim-cat-blk').forEach(b => b.style.display = '');
        let n = 0;
        document.querySelectorAll('.aim-row').forEach(r => {
            const ok = r.dataset.kw.includes(q);
            r.style.display = ok ? '' : 'none';
            if (ok) n++;
        });
        document.querySelectorAll('.aim-cat-blk').forEach(b => {
            const any = [...b.querySelectorAll('.aim-row')].some(r => r.style.display !== 'none');
            b.style.display = any ? '' : 'none';
        });
        lbl.textContent = '"' + q + '" — ' + n + ' ürün';
        lbl.classList.toggle('on', true);
        document.getElementById('aimNoRes').classList.toggle('on', n === 0);
    };

    const _origOpenModal = window.openModal;
    window.openModal = function (id) {
        _origOpenModal(id);
        if (id === 'addItemModal') {
            aimBasket = [];
            aimCurId = null;
            aimQty = 1;
            document.querySelectorAll('.aim-row.picked').forEach(r => r.classList.remove('picked'));
            document.getElementById('aimPh').style.display = '';
            document.getElementById('aimForm').style.display = 'none';
            document.getElementById('aimQ').value = '';
            window.aimSearch('');
            window.aimCat('all', document.querySelector('.aim-ctab[data-c="all"]'));
            aimRenderBasket();
        }
    };

    // ═══════════════════════════════════════════════════════════
    // ÖDEME MODAL: kalem seçimi (piselState)
    // ═══════════════════════════════════════════════════════════
    const piselState = {};
    document.querySelectorAll('.pisel-row').forEach(row => {
        const id = parseInt(row.dataset.itemId);
        const max = parseInt(row.dataset.maxQty);
        const up = parseFloat(row.dataset.unitPrice);
        piselState[id] = { selected: 0, max, up };
    });

    window.piselChange = function (id, delta) {
        const s = piselState[id];
        if (!s || s.max === 0) return;
        s.selected = Math.max(0, Math.min(s.max, s.selected + delta));

        const qEl = document.getElementById('pisel-qty-' + id);
        qEl.textContent = s.selected;
        qEl.classList.toggle('has-sel', s.selected > 0);

        const sub = s.selected * s.up;
        document.getElementById('pisel-sub-' + id).textContent = s.selected > 0 ? fmt(sub) : '—';

        document.getElementById('pisel-minus-' + id).style.opacity = s.selected === 0 ? '0.3' : '1';
        document.getElementById('pisel-plus-' + id).style.opacity = s.selected === s.max ? '0.3' : '1';

        updatePiselTotal();
    };

    function updatePiselTotal() {
        let t = 0;
        Object.values(piselState).forEach(s => { t += s.selected * s.up; });
        document.getElementById('piselTotalVal').textContent = fmt(t);
        document.getElementById('piselApplyBtn').disabled = t <= 0;
    }

    window.applyPisel = function () {
        let t = 0;
        Object.values(piselState).forEach(s => { t += s.selected * s.up; });
        if (t <= 0) return;
        document.getElementById('payAmountDisplay').value = t.toFixed(2).replace('.', ',');
        window.updateChange();
    };

    // ── Ödeme formu helpers ──
    window.selectMethod = function (btn, method) {
        document.querySelectorAll('.method-btn').forEach(b => b.classList.remove('active'));
        btn.classList.add('active');
        document.getElementById('selectedMethod').value = method;
        currentMethod = method;
        document.getElementById('changeRow').style.display = method === 'cash' ? 'block' : 'none';
        window.updateChange();
    };

    // ═══════════════════════════════════════════════════════════
    // YENİ: İndirim tipi değişince badge ve placeholder güncelle
    // ═══════════════════════════════════════════════════════════
    window.onDiscountTypeChange = function () {
        const isPercent = getDiscountType() === 'percent';
        document.getElementById('disc-unit-badge').textContent = isPercent ? '%' : '₺';
        document.getElementById('discountDisplay').placeholder = isPercent ? '10' : '0';
        document.getElementById('discountDisplay').value = '';
        // Önizlemeyi ve kalan tutarı sıfırla
        document.getElementById('disc-preview').style.display = 'none';
        document.getElementById('pm-disc-row').style.display = 'none';
        updateRemainingUI(0);
        window.updateChange();
    };

    function getDiscountType() {
        const checked = document.querySelector('input[name="discountType"]:checked');
        return checked ? checked.value : 'amount';
    }

    /**
     * Girilen indirim değerini, seçilen tipe göre TL tutarına çevirir.
     * Tutarın toplam adisyon tutarını aşmamasını garanti eder.
     */
    function computeDiscountTL() {
        const raw = parseLD(document.getElementById('discountDisplay').value);
        const type = getDiscountType();
        let disc;

        if (type === 'percent') {
            // Yüzdeyi 0–100 ile sınırla, sonra TL'ye çevir
            const pct = Math.min(100, Math.max(0, raw));
            disc = Math.round((orderTotal * pct / 100) * 100) / 100;
        } else {
            disc = Math.max(0, raw);
        }

        // Adisyon toplamından büyük olamaz
        return Math.min(disc, orderTotal);
    }

    function updateRemainingUI(discTL) {
        const net = Math.max(0, orderTotal - discTL - alreadyPaid);
        document.getElementById('pm-remaining').textContent = fmt(net);
        document.getElementById('fillAmountLabel').textContent = fmt(net);
    }

    // ── Ana indirim hesap fonksiyonu (oninput'tan tetiklenir) ──
    window.updateRemaining = function () {
        const discTL = computeDiscountTL();
        const netFull = Math.max(0, orderTotal - discTL); // ödenmesi gereken toplam
        const rawInput = parseLD(document.getElementById('discountDisplay').value);

        // Özet kutu satırı
        const dr = document.getElementById('pm-disc-row');
        if (discTL > 0) {
            dr.style.display = 'flex';
            document.getElementById('pm-disc-val').textContent = '−' + fmt(discTL);
        } else {
            dr.style.display = 'none';
        }

        // Anlık hesap önizlemesi
        const preview = document.getElementById('disc-preview');
        if (rawInput > 0) {
            preview.style.display = 'block';
            document.getElementById('disc-computed-amount').textContent = discTL.toFixed(2).replace('.', ',');
            document.getElementById('net-amount').textContent = netFull.toFixed(2).replace('.', ',');
        } else {
            preview.style.display = 'none';
        }

        updateRemainingUI(discTL);
        window.updateChange();
    };

    window.fillRemaining = function () {
        const discTL = computeDiscountTL();
        const rem = Math.max(0, orderTotal - discTL - alreadyPaid);
        document.getElementById('payAmountDisplay').value = rem.toFixed(2).replace('.', ',');
        window.updateChange();
    };

    window.updateChange = function () {
        if (currentMethod !== 'cash') return;
        const discTL = computeDiscountTL();
        const rem = Math.max(0, orderTotal - discTL - alreadyPaid);
        const entered = parseLD(document.getElementById('payAmountDisplay').value);
        const change = Math.max(0, entered - rem);
        document.getElementById('changeDisplay').textContent = fmt(change);
    };

    // ── Ödeme gönder — AddPayment ──
    window.submitPayment = async function () {
        const payVal = parseLD(document.getElementById('payAmountDisplay').value);
        const rawDisc = parseLD(document.getElementById('discountDisplay').value);
        const discType = getDiscountType();
        const err = document.getElementById('err-amount');

        if (payVal <= 0) { err.style.display = 'block'; return; }
        err.style.display = 'none';

        const payerName = document.getElementById('payerNameInput')?.value || '';
        const method = document.getElementById('selectedMethod').value;

        const paidItems = Object.entries(piselState)
            .filter(([, s]) => s.selected > 0)
            .map(([id, s]) => ({ orderItemId: parseInt(id), quantity: s.selected }));

        // ── YENİ payload: discountType + discountValue ──
        const payload = {
            orderId,
            payerName: payerName.trim() || null,
            paymentMethod: method,
            paymentAmount: payVal,
            discountType: discType,          // "amount" veya "percent"
            discountValue: rawDisc,           // Kullanıcının girdiği ham değer
            discountAmount: 0,               // Backend hesaplar, burası artık kullanılmıyor
            paidItems: paidItems.length > 0 ? paidItems : null
        };

        const submitBtn = document.querySelector('.pay-modal-actions .btn-primary');
        if (submitBtn) { submitBtn.disabled = true; submitBtn.textContent = '⏳ Kaydediliyor...'; }

        try {
            const data = await postJson(window.APP_URLS.ordersAddPayment, payload);

            if (data.success) {
                if (data.redirectUrl) {
                    window.location.href = data.redirectUrl;
                } else {
                    window.closeModal('payModal');
                    location.reload();
                }
            } else {
                alert('Hata: ' + (data.message || 'Bilinmeyen hata'));
                if (submitBtn) { submitBtn.disabled = false; submitBtn.textContent = '💾 Ödemeyi Kaydet'; }
            }
        } catch (e) {
            if (e.message !== 'Unauthorized') {
                alert('İstek gönderilemedi.');
                if (submitBtn) { submitBtn.disabled = false; submitBtn.textContent = '💾 Ödemeyi Kaydet'; }
            }
        }
    };

    // ── Adisyon Kapat (CloseZero) ──
    window.submitCloseZero = async function () {
        if (!confirm('Adisyon iptal edilmiş sayılacak ve masa boşaltılacak. Devam edilsin mi?')) return;

        const payload = { orderId, paymentMethod: 'cash', paymentAmount: 0 };

        try {
            const data = await postJson(window.APP_URLS.ordersCloseZero, payload);
            if (data.success) { window.location.href = data.redirectUrl; }
            else { alert('Hata: ' + (data.message || 'Bilinmeyen hata')); }
        } catch (e) {
            if (e.message !== 'Unauthorized') alert('İstek gönderilemedi.');
        }
    };

    // ═══════════════════════════════════════════════════════════
    // İPTAL MODAL — CancelItem
    // ═══════════════════════════════════════════════════════════
    let cimMaxQty = 1;
    let cimUnitPrice = 0;
    let cimQty = 1;
    let cimTracksStock = false;
    let cimCurrentItemId = null;

    window.openCancelModal = function (itemId, name, unitPrice, maxQty, tracksStock) {
        cimCurrentItemId = itemId;
        cimMaxQty = maxQty;
        cimUnitPrice = unitPrice;
        cimTracksStock = tracksStock;
        cimQty = 1;

        document.getElementById('cimProductName').textContent = name;
        document.getElementById('cimReason').value = '';
        document.getElementById('cimQtyMax').textContent = '(maks. ' + maxQty + ')';
        document.getElementById('cimWasteField').style.display = tracksStock ? '' : 'none';

        document.querySelectorAll('#cancelItemModal input[type="radio"]')
            .forEach(r => r.checked = r.value === 'false');

        cimRefresh();
        window.openModal('cancelItemModal');
    };

    window.cimDelta = function (d) {
        cimQty = Math.max(1, Math.min(cimMaxQty, cimQty + d));
        cimRefresh();
    };

    function cimRefresh() {
        document.getElementById('cimQtyNum').textContent = cimQty;
        document.getElementById('cimRefundAmt').textContent = fmt(cimUnitPrice * cimQty);
        document.getElementById('cimConfirmBtn').textContent =
            '✕ ' + cimQty + ' Adet İptal Et (−' + fmt(cimUnitPrice * cimQty) + ')';
    }

    window.cimWasteChange = function (isWasted) {
        document.getElementById('cimIsWastedHidden').value = isWasted ? 'true' : 'false';
    };

    window.submitCancelItem = async function () {
        const cancelReason = document.getElementById('cimReason').value.trim() || null;
        const isWastedVal = document.getElementById('cimIsWastedHidden').value === 'true';

        const payload = {
            orderItemId: cimCurrentItemId,
            orderId,
            cancelQty: cimQty,
            cancelReason,
            isWasted: cimTracksStock ? isWastedVal : null
        };

        const confirmBtn = document.getElementById('cimConfirmBtn');
        confirmBtn.disabled = true;

        try {
            const data = await postJson(window.APP_URLS.ordersCancelItem, payload);

            if (data.success) {
                window.closeModal('cancelItemModal');
                location.reload();
            } else {
                alert('Hata: ' + (data.message || 'Bilinmeyen hata'));
                confirmBtn.disabled = false;
            }
        } catch (e) {
            if (e.message !== 'Unauthorized') {
                alert('İstek gönderilemedi.');
                confirmBtn.disabled = false;
            }
        }
    };

    // Uyarıları gizleme
    setTimeout(() => {
        document.querySelectorAll('.alert').forEach(a => {
            a.style.transition = 'opacity .5s';
            a.style.opacity = '0';
            setTimeout(() => a.remove(), 500);
        });
    }, 3000);

});
// ═══════════════════════════════════════════════════════════
// [SPRINT-4] SignalR — Detail sayfasında OrderReady bildirimi
// Mutfak "Hazır" dediğinde garson, sayfayı yenilemeden "Servis
// Edildi" butonunu görür (location.reload ile sayfa güncellenir).
// ═══════════════════════════════════════════════════════════
(function initDetailSignalR() {
    if (typeof signalR === 'undefined') return;

    const cfg = JSON.parse(document.getElementById('orderConfigData').textContent);
    const currentOrderId = parseInt(cfg.orderId);

    const conn = new signalR.HubConnectionBuilder()
        .withUrl('/hubs/restaurant')
        .withAutomaticReconnect([0, 2000, 5000])
        .configureLogging(signalR.LogLevel.Warning)
        .build();

    // Mutfak bu adisyondaki bir kalemi Hazır yaptı → sayfayı yenile
    conn.on('OrderReady', function (payload) {
        if (parseInt(payload.orderId) === currentOrderId) location.reload();
    });

    // Başka terminalden Served yapıldı → yenile
    conn.on('OrderServed', function (payload) {
        if (parseInt(payload.orderId) === currentOrderId) location.reload();
    });

    async function start() {
        try { await conn.start(); }
        catch { setTimeout(start, 5000); }
    }
    start();
})();
/* ════════════════════════════════════════════════════════════
   STATE
════════════════════════════════════════════════════════════ */
// JSON Data Island'dan C# verisini oku
const pageData = JSON.parse(document.getElementById('createPageData').textContent);
const TABLE_ID = pageData.tableId;

const basket = {}; // {[id]: {id, name, price, qty, note, maxStock, trackStock}}

/* ════════════════════════════════════════════════════════════
   TOAST (Sağ-alt stok uyarıları) — Kural 1 & 2
════════════════════════════════════════════════════════════ */
function showCreateToast(message, type = 'warning') {
    const container = document.getElementById('createToastContainer');
    if (!container) return;

    const icons = { success: '✅', error: '❌', warning: '⚠️' };
    const toast = document.createElement('div');
    toast.className = `cr-toast cr-toast-${type}`;
    toast.innerHTML = `<span>${icons[type] || '⚠️'}</span><span>${message}</span>`;
    container.appendChild(toast);

    requestAnimationFrame(() => toast.classList.add('cr-toast-show'));
    setTimeout(() => {
        toast.classList.remove('cr-toast-show');
        setTimeout(() => toast.remove(), 400);
    }, 3500);
}

/* ════════════════════════════════════════════════════════════
   ÜRÜN EKLE / ÇIKAR
════════════════════════════════════════════════════════════ */
function addItem(id) {
    const card = document.getElementById('icard-' + id);
    if (!card) return;

    // KAPAK 1 — CSS pointer-events:none disabled kartı zaten bloke eder.
    // Bu JS guard CSS'nin atlatıldığı senaryolara karşı ikinci güvencedir.
    if (card.classList.contains('item-card-disabled')) return;

    const name = card.dataset.name;
    const price = parseFloat(card.dataset.price);

    // KAPAK 2 — NaN-güvenli stok okuma
    // data-track yoksa/false → sınırsız (Infinity)
    // data-stock yoksa/NaN → sınırsız (Infinity)
    const trackStock = (card.dataset.track ?? 'false') === 'true';
    const rawStock = parseInt(card.dataset.stock ?? '', 10);
    const maxStock = (trackStock && Number.isInteger(rawStock) && rawStock >= 0)
        ? rawStock
        : Infinity;

    // KAPAK 3 — Stok sınırı kontrolü (sadece trackStock=true ve sonlu maxStock ise)
    const currentQty = basket[id] ? basket[id].qty : 0;
    if (trackStock && Number.isFinite(maxStock) && currentQty >= maxStock) {
        showCreateToast(`Ürün stoğu = ${maxStock} kadar ekleme yapabilirsiniz.`);
        return;
    }

    if (basket[id]) {
        basket[id].qty++;
    } else {
        basket[id] = { id, name, price, qty: 1, note: '', maxStock, trackStock };
    }

    card.classList.add('selected');
    updateBadge(id);

    card.style.transform = 'scale(.94)';
    setTimeout(() => card.style.transform = '', 120);

    render();
}

function removeItem(id) {
    delete basket[id];
    const card = document.getElementById('icard-' + id);
    if (card) {
        card.classList.remove('selected');
        updateBadge(id);
    }
    render();
}

function changeQty(id, delta) {
    if (!basket[id]) return;

    // Kural 1: + yönünde stok sınırını aşma
    if (delta > 0) {
        // Basket'ta saklı trackStock/maxStock kullan (DOM'u tekrar okumaya gerek yok)
        const trackStock = basket[id].trackStock ?? false;
        const maxStock = basket[id].maxStock ?? Infinity;
        const qty = basket[id].qty;
        // Sadece: stok takibinde VE maxStock sonlu bir sayıysa sınırla
        if (trackStock && Number.isFinite(maxStock) && qty >= maxStock) {
            showCreateToast(`Ürün stoğu = ${maxStock} kadar ekleme yapabilirsiniz.`);
            return;
        }
    }

    basket[id].qty += delta;
    if (basket[id].qty < 1) { removeItem(id); return; }
    updateBadge(id);
    render();
}

function updateNote(id, val) {
    if (basket[id]) basket[id].note = val;
    // syncHidden kaldırıldı — veri doğrudan basket objesinde tutulur
}

function updateBadge(id) {
    const badge = document.getElementById('badge-' + id);
    if (!badge) return;
    const item = basket[id];
    badge.textContent = item ? item.qty : '';
}

/* ════════════════════════════════════════════════════════════
   RENDER
════════════════════════════════════════════════════════════ */
function render() {
    const items = Object.values(basket);
    const container = document.getElementById('cartItems');
    const empty = document.getElementById('cartEmpty');
    const count = document.getElementById('cartCount');
    const total = document.getElementById('cartTotal');
    const btnOpen = document.getElementById('btnOpen');

    container.querySelectorAll('.citem').forEach(el => el.remove());

    const totalQty = items.reduce((s, i) => s + i.qty, 0);
    const totalAmt = items.reduce((s, i) => s + i.price * i.qty, 0);

    count.textContent = totalQty;
    count.classList.remove('pop');
    void count.offsetWidth;
    count.classList.add('pop');

    if (items.length === 0) {
        empty.style.display = 'block';
        btnOpen.disabled = true;
    } else {
        empty.style.display = 'none';
        btnOpen.disabled = false;

        items.forEach(item => {
            const div = document.createElement('div');
            div.className = 'citem';
            div.innerHTML = `
    <div class="citem-row1">
        <span class="citem-name" title="${esc(item.name)}">${esc(item.name)}</span>
        <div class="citem-qty-ctrl">
            <button type="button" class="cqbtn minus" onclick="changeQty(${item.id},-1)">−</button>
            <span class="citem-qty">${item.qty}</span>
            <button type="button" class="cqbtn" onclick="changeQty(${item.id},1)">+</button>
        </div>
        <span class="citem-price">₺${(item.price * item.qty).toFixed(2).replace('.', ',')}</span>
        <button type="button" class="citem-del" onclick="removeItem(${item.id})" title="Kaldır">×</button>
    </div>
    <input class="citem-note" type="text"
        placeholder="Not: acısız, az pişmiş..."
        value="${esc(item.note)}"
        oninput="updateNote(${item.id}, this.value)"
        maxlength="200" />
    `;
            container.appendChild(div);
        });
    }

    total.classList.remove('bump');
    void total.offsetWidth;
    total.classList.add('bump');
    total.textContent = '₺' + totalAmt.toFixed(2).replace('.', ',');
}

/* ════════════════════════════════════════════════════════════
   FORM SUBMIT — Fetch API
════════════════════════════════════════════════════════════ */
async function submitOrder() {
    if (Object.keys(basket).length === 0) {
        const cart = document.querySelector('.pos-cart');
        cart.style.animation = 'none';
        void cart.offsetWidth;
        cart.style.animation = 'shake .35s ease';
        return;
    }

    const btnOpen = document.getElementById('btnOpen');
    btnOpen.disabled = true;
    btnOpen.textContent = '⏳ Açılıyor...';

    // DTO'daki OrderCreateDto ile eşleşen payload
    const payload = {
        tableId: TABLE_ID,
        orderNote: document.getElementById('orderNoteInput').value.trim() || null,
        items: Object.values(basket).map(item => ({
            menuItemId: item.id,
            quantity: item.qty,
            note: item.note || null
        }))
    };

    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

    try {
        const res = await fetch(window.APP_URLS.ordersCreate, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token
            },
            body: JSON.stringify(payload)
        });

        const data = await res.json();

        if (data.success) {
            window.location.href = data.redirectUrl;
        } else {
            // Backend stok hatası → toast (Kural 3 yanıtı)
            showCreateToast(data.message || 'Bilinmeyen hata', 'error');
            btnOpen.disabled = false;
            btnOpen.textContent = '✓  Adisyonu Aç';
        }
    } catch (e) {
        showCreateToast('İstek gönderilemedi. Lütfen sayfayı yenileyip tekrar deneyin.', 'error');
        btnOpen.disabled = false;
        btnOpen.textContent = '✓  Adisyonu Aç';
    }
}

/* ════════════════════════════════════════════════════════════
   KATEGORİ FİLTRE
════════════════════════════════════════════════════════════ */
let activeCat = 'all';

function filterCat(catKey, btn) {
    activeCat = catKey;

    document.getElementById('searchInput').value = '';
    document.getElementById('searchLabel').classList.remove('show');

    document.querySelectorAll('.cat-btn').forEach(b => b.classList.remove('active'));
    btn.classList.add('active');

    document.querySelectorAll('.item-card').forEach(c => c.style.display = '');

    document.querySelectorAll('.cat-block').forEach(block => {
        const show = catKey === 'all' || block.id === catKey;
        block.style.display = show ? '' : 'none';
    });

    document.getElementById('itemsEmpty').classList.remove('visible');
}

/* ════════════════════════════════════════════════════════════
   ARAMA
════════════════════════════════════════════════════════════ */
function doSearch(q) {
    q = q.toLowerCase().trim();
    const label = document.getElementById('searchLabel');

    if (!q) {
        filterCat(activeCat, document.querySelector(`.cat-btn[data-cat="${activeCat}"]`));
        return;
    }

    document.querySelectorAll('.cat-btn').forEach(b => b.classList.remove('active'));
    document.querySelectorAll('.cat-block').forEach(b => b.style.display = '');

    let found = 0;
    document.querySelectorAll('.item-card').forEach(card => {
        const match = card.dataset.keywords.includes(q);
        card.style.display = match ? '' : 'none';
        if (match) found++;
    });

    document.querySelectorAll('.cat-block').forEach(block => {
        const anyVisible = [...block.querySelectorAll('.item-card')]
            .some(c => c.style.display !== 'none');
        block.style.display = anyVisible ? '' : 'none';
    });

    label.textContent = `"${q}" — ${found} ürün bulundu`;
    label.classList.toggle('show', true);

    const emptyEl = document.getElementById('itemsEmpty');
    emptyEl.classList.toggle('visible', found === 0);
}

document.getElementById('searchInput').addEventListener('keydown', e => {
    if (e.key === 'Escape') {
        e.target.value = '';
        doSearch('');
        e.target.blur();
    }
});

/* ════════════════════════════════════════════════════════════
   YARDIMCI
════════════════════════════════════════════════════════════ */
function esc(str) {
    return String(str)
        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

// Sepet shake animasyonu
const shakeStyle = document.createElement('style');
shakeStyle.textContent = `
@keyframes shake {
    0%,100%{transform:translateX(0)}
    20%{transform:translateX(-5px)}
    40%{transform:translateX(5px)}
    60%{transform:translateX(-4px)}
    80%{transform:translateX(4px)}
}`;
document.head.appendChild(shakeStyle);
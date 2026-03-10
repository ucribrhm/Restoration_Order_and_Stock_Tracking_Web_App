// ── Dil Sekmesi ─────────────────────────────────────────────────────
window.switchTab = function (clickedBtn, tablistId) {
    const tablist = document.getElementById(tablistId);
    if (!tablist) return;

    tablist.querySelectorAll('.lang-tab-btn').forEach(btn => {
        btn.classList.remove('active');
        btn.setAttribute('aria-selected', 'false');
    });

    clickedBtn.classList.add('active');
    clickedBtn.setAttribute('aria-selected', 'true');

    const modal = tablist.closest('.modal');
    if (!modal) return;

    modal.querySelectorAll('.lang-pane').forEach(pane => { pane.style.display = 'none'; });

    const targetPane = modal.querySelector('#' + clickedBtn.getAttribute('data-target'));
    if (targetPane) targetPane.style.display = '';
};

function resetTabsToTR(modalId, tablistId, panePrefix) {
    const modal = document.getElementById(modalId);
    const tablist = document.getElementById(tablistId);
    if (!modal || !tablist) return;

    tablist.querySelectorAll('.lang-tab-btn').forEach(btn => {
        btn.classList.remove('active');
        btn.setAttribute('aria-selected', 'false');
    });

    const firstBtn = tablist.querySelector('.lang-tab-btn');
    if (firstBtn) { firstBtn.classList.add('active'); firstBtn.setAttribute('aria-selected', 'true'); }

    modal.querySelectorAll('.lang-pane').forEach(p => p.style.display = 'none');
    const trPane = modal.querySelector(`#${panePrefix}-pane-tr`);
    if (trPane) trPane.style.display = '';
}

// ── Modal Helpers ────────────────────────────────────────────────────
function openModal(id) { document.getElementById(id).classList.add('open'); }
function closeModal(id) { document.getElementById(id).classList.remove('open'); }

document.querySelectorAll('.modal-overlay').forEach(o =>
    o.addEventListener('click', e => { if (e.target === o) o.classList.remove('open'); })
);

function showToast(msg, type = 'success') {
    const c = document.getElementById('toastContainer');
    const t = document.createElement('div');
    t.className = `toast toast-${type}`;
    t.innerHTML = `<span>${type === 'success' ? '✅' : '❌'}</span><span>${msg}</span>`;
    c.appendChild(t);
    setTimeout(() => t.remove(), 3500);
}

function getToken() {
    return document.querySelector('input[name="__RequestVerificationToken"]').value;
}

// ── Görsel Önizleme ──────────────────────────────────────────────────
function bindImagePreview(inputId, previewWrapperId, previewImgId, previewNameId) {
    const input = document.getElementById(inputId);
    if (!input) return;
    input.addEventListener('change', function () {
        const file = this.files[0];
        const wrap = document.getElementById(previewWrapperId);
        const img = document.getElementById(previewImgId);
        const name = document.getElementById(previewNameId);
        if (file) {
            const reader = new FileReader();
            reader.onload = e => { img.src = e.target.result; };
            reader.readAsDataURL(file);
            name.textContent = file.name;
            wrap.style.display = 'block';
        } else {
            wrap.style.display = 'none';
        }
    });
}
bindImagePreview('c_imageFile', 'c_preview', 'c_previewImg', 'c_previewName');
bindImagePreview('e_imageFile', 'e_preview', 'e_previewImg', 'e_previewName');

// ── Tablo Filtresi ───────────────────────────────────────────────────
function filterTable() {
    const search = document.getElementById('searchInput').value.toLowerCase();
    const cat = document.getElementById('catFilter').value;
    const status = document.getElementById('statusFilter').value;
    document.querySelectorAll('#menuTable tbody tr[data-name]').forEach(row => {
        const matchName = row.dataset.name.includes(search);
        const matchCat = !cat || row.dataset.cat === cat;
        const matchStatus = !status || row.dataset.status === status;
        row.style.display = (matchName && matchCat && matchStatus) ? '' : 'none';
    });
}

// ══════════════════════════════════════════════════════════════════════
//  CREATE
// ══════════════════════════════════════════════════════════════════════
function openCreateModal() {
    document.getElementById('createForm').reset();
    document.getElementById('c_isAvailable').checked = true;
    document.getElementById('c_preview').style.display = 'none';
    resetTabsToTR('createModal', 'createMenuLangTabs', 'cm');
    openModal('createModal');
}

document.getElementById('createForm')?.addEventListener('submit', async e => {
    e.preventDefault();
    const btn = e.submitter;
    const origText = btn.textContent;
    btn.disabled = true;
    btn.textContent = '⏳ Kaydediliyor…';

    // FormData: hem çok dilli metin alanları hem imageFile multipart ile gönderilir
    const fd = new FormData();
    fd.append('menuItemName', document.getElementById('c_name').value.trim());
    fd.append('nameEn', document.getElementById('c_nameEn').value.trim() || '');
    fd.append('nameAr', document.getElementById('c_nameAr').value.trim() || '');
    fd.append('nameRu', document.getElementById('c_nameRu').value.trim() || '');
    fd.append('categoryId', document.getElementById('c_categoryId').value);
    fd.append('menuItemPriceStr', document.getElementById('c_price').value);
    fd.append('description', document.getElementById('c_description').value.trim() || '');
    fd.append('descriptionEn', document.getElementById('c_descriptionEn').value.trim() || '');
    fd.append('descriptionAr', document.getElementById('c_descriptionAr').value.trim() || '');
    fd.append('descriptionRu', document.getElementById('c_descriptionRu').value.trim() || '');
    fd.append('detailedDescription', document.getElementById('c_detailedDescription').value.trim() || '');
    fd.append('stockQuantity', document.getElementById('c_stock').value);
    fd.append('trackStock', document.getElementById('c_trackStock').checked ? 'true' : 'false');
    fd.append('isAvailable', document.getElementById('c_isAvailable').checked ? 'true' : 'false');
    fd.append('displayOrder', document.getElementById('c_displayOrder').value || '0');

    const imgFile = document.getElementById('c_imageFile');
    if (imgFile?.files[0]) fd.append('imageFile', imgFile.files[0]);

    try {
        const res = await fetch(window.APP_URLS.menuCreate, {
            method: 'POST',
            headers: { 'RequestVerificationToken': getToken() },
            // Content-Type AYARLANMAZ — tarayıcı multipart boundary'yi otomatik ekler
            body: fd
        });
        const data = await res.json();
        btn.disabled = false;
        btn.textContent = origText;

        if (data.success) {
            closeModal('createModal');
            showToast(data.message, 'success');
            setTimeout(() => location.reload(), 800);
        } else {
            showToast(data.message, 'error');
        }
    } catch {
        btn.disabled = false;
        btn.textContent = origText;
        showToast('Bağlantı hatası oluştu.', 'error');
    }
});

// ══════════════════════════════════════════════════════════════════════
//  EDIT
// ══════════════════════════════════════════════════════════════════════
function removeEditImage() {
    document.getElementById('e_removeImage').value = 'true';
    document.getElementById('e_currentImgWrap').style.display = 'none';
    document.getElementById('e_uploadHint').textContent = '🖼️  Tıkla veya sürükle';
}

// Yeni dosya seçilince removeImage bayrağını geri al
document.getElementById('e_imageFile')?.addEventListener('change', function () {
    if (this.files[0]) {
        document.getElementById('e_removeImage').value = 'false';
    }
});

async function openEditModal(id) {
    try {
        const res = await fetch(`${window.APP_URLS.menuGetById}/${id}`);
        const data = await res.json();
        if (!data.success) { showToast('Veri alınamadı.', 'error'); return; }

        // Kimlik + temel alanlar
        document.getElementById('e_id').value = data.menuItemId;
        document.getElementById('e_price').value = data.menuItemPrice;
        document.getElementById('e_stock').value = data.stockQuantity;
        document.getElementById('e_trackStock').checked = data.trackStock;
        document.getElementById('e_isAvailable').checked = data.isAvailable;
        document.getElementById('e_categoryId').value = data.categoryId;
        document.getElementById('e_displayOrder').value = data.displayOrder ?? 0;

        // Çok dilli alanlar
        document.getElementById('e_name').value = data.menuItemName ?? '';
        document.getElementById('e_nameEn').value = data.nameEn ?? '';
        document.getElementById('e_nameAr').value = data.nameAr ?? '';
        document.getElementById('e_nameRu').value = data.nameRu ?? '';
        document.getElementById('e_description').value = data.description ?? '';
        document.getElementById('e_descriptionEn').value = data.descriptionEn ?? '';
        document.getElementById('e_descriptionAr').value = data.descriptionAr ?? '';
        document.getElementById('e_descriptionRu').value = data.descriptionRu ?? '';
        document.getElementById('e_detailedDescription').value = data.detailedDescription ?? '';

        // Görsel sıfırla
        document.getElementById('e_removeImage').value = 'false';
        document.getElementById('e_preview').style.display = 'none';
        const fileInput = document.getElementById('e_imageFile');
        if (fileInput) fileInput.value = '';

        // Mevcut görsel varsa göster
        const wrap = document.getElementById('e_currentImgWrap');
        const img = document.getElementById('e_currentImg');
        if (data.imagePath) {
            img.src = data.imagePath;
            wrap.style.display = 'block';
            document.getElementById('e_uploadHint').textContent = '🖼️  Yeni fotoğraf seçmek için tıkla';
        } else {
            wrap.style.display = 'none';
            document.getElementById('e_uploadHint').textContent = '🖼️  Tıkla veya sürükle';
        }

        resetTabsToTR('editModal', 'editMenuLangTabs', 'em');
        openModal('editModal');

    } catch {
        showToast('Veri çekilirken hata oluştu.', 'error');
    }
}

document.getElementById('editForm')?.addEventListener('submit', async e => {
    e.preventDefault();
    const btn = e.submitter;
    const origText = btn.textContent;
    btn.disabled = true;
    btn.textContent = '⏳ Güncelleniyor…';

    const fd = new FormData();
    fd.append('id', document.getElementById('e_id').value);
    fd.append('menuItemName', document.getElementById('e_name').value.trim());
    fd.append('nameEn', document.getElementById('e_nameEn').value.trim() || '');
    fd.append('nameAr', document.getElementById('e_nameAr').value.trim() || '');
    fd.append('nameRu', document.getElementById('e_nameRu').value.trim() || '');
    fd.append('categoryId', document.getElementById('e_categoryId').value);
    fd.append('menuItemPriceStr', document.getElementById('e_price').value);
    fd.append('description', document.getElementById('e_description').value.trim() || '');
    fd.append('descriptionEn', document.getElementById('e_descriptionEn').value.trim() || '');
    fd.append('descriptionAr', document.getElementById('e_descriptionAr').value.trim() || '');
    fd.append('descriptionRu', document.getElementById('e_descriptionRu').value.trim() || '');
    fd.append('detailedDescription', document.getElementById('e_detailedDescription').value.trim() || '');
    fd.append('stockQuantity', document.getElementById('e_stock').value);
    fd.append('trackStock', document.getElementById('e_trackStock').checked ? 'true' : 'false');
    fd.append('isAvailable', document.getElementById('e_isAvailable').checked ? 'true' : 'false');
    fd.append('displayOrder', document.getElementById('e_displayOrder').value || '0');
    fd.append('removeImage', document.getElementById('e_removeImage').value);

    const imgFile = document.getElementById('e_imageFile');
    if (imgFile?.files[0]) fd.append('imageFile', imgFile.files[0]);

    try {
        const res = await fetch(window.APP_URLS.menuEdit, {
            method: 'POST',
            headers: { 'RequestVerificationToken': getToken() },
            body: fd
        });
        const data = await res.json();
        btn.disabled = false;
        btn.textContent = origText;

        if (data.success) {
            closeModal('editModal');
            showToast(data.message, 'success');
            setTimeout(() => location.reload(), 800);
        } else {
            showToast(data.message, 'error');
        }
    } catch {
        btn.disabled = false;
        btn.textContent = origText;
        showToast('Bağlantı hatası oluştu.', 'error');
    }
});

// ══════════════════════════════════════════════════════════════════════
//  DELETE
// ══════════════════════════════════════════════════════════════════════
function openDeleteModal(id, name) {
    document.getElementById('d_id').value = id;
    document.getElementById('d_name').textContent = name;
    openModal('deleteModal');
}

document.getElementById('deleteForm').addEventListener('submit', async e => {
    e.preventDefault();
    const btn = e.submitter;
    btn.disabled = true;

    const body = new URLSearchParams({
        id: document.getElementById('d_id').value,
        __RequestVerificationToken: getToken()
    });

    try {
        const res = await fetch(window.APP_URLS.menuDelete, { method: 'POST', body });
        const data = await res.json();
        btn.disabled = false;
        closeModal('deleteModal');
        showToast(data.message, data.success ? 'success' : 'error');
        if (data.success) setTimeout(() => location.reload(), 800);
    } catch {
        btn.disabled = false;
        closeModal('deleteModal');
        showToast('Bağlantı hatası oluştu.', 'error');
    }
});
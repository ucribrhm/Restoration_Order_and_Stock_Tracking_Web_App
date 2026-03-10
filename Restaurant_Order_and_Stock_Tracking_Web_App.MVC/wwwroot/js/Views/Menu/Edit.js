// ── Dil sekme geçişi ──────────────────────────────────────────
function switchLangTab(lang, btn) {
    document.querySelectorAll('.lang-tab').forEach(b => b.classList.remove('active'));
    document.querySelectorAll('.lang-panel').forEach(p => p.style.display = 'none');
    btn.classList.add('active');
    document.getElementById('panel-' + lang).style.display = '';
}

// ── Form gönder ───────────────────────────────────────────────
document.getElementById('editForm')?.addEventListener('submit', async e => {
    e.preventDefault();
    const btn = e.submitter;
    btn.disabled = true;

    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

    const payload = {
        id: parseInt(document.getElementById('menuItemId').value) || 0,
        menuItemName: document.getElementById('menuItemName').value.trim(),
        nameEn: document.getElementById('nameEn').value.trim(),
        nameAr: document.getElementById('nameAr').value.trim(),
        nameRu: document.getElementById('nameRu').value.trim(),
        categoryId: parseInt(document.getElementById('categoryId').value) || 0,
        menuItemPriceStr: document.getElementById('menuItemPrice').value,
        description: document.getElementById('description').value.trim(),
        descriptionEn: document.getElementById('descriptionEn').value.trim(),
        descriptionAr: document.getElementById('descriptionAr').value.trim(),
        descriptionRu: document.getElementById('descriptionRu').value.trim(),
        stockQuantity: parseInt(document.getElementById('stockQuantity').value) || 0,
        trackStock: document.getElementById('trackStock').checked,
        isAvailable: document.getElementById('isAvailable').checked
    };

    try {
        const res = await fetch(window.APP_URLS.menuEdit, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token
            },
            body: JSON.stringify(payload)
        });
        const data = await res.json();
        btn.disabled = false;

        if (data.success) {
            window.location.href = window.APP_URLS.menuIndex;
        } else {
            const box = document.getElementById('alertBox');
            box.textContent = data.message;
            box.style.display = 'block';
        }
    } catch {
        btn.disabled = false;
        document.getElementById('alertBox').textContent = 'Bağlantı hatası oluştu.';
        document.getElementById('alertBox').style.display = 'block';
    }

});
// ─── Mevcut görseli kaldır ────────────────────────────────────────────────────
function removeCurrentImage() {
    document.getElementById('currentImageWrap')?.remove();
    document.getElementById('removeImageFlag').value = 'true';
}

// ─── Yeni görsel önizleme ─────────────────────────────────────────────────────
document.getElementById('imageFile')?.addEventListener('change', function () {
    const file = this.files[0];
    const wrap = document.getElementById('previewWrap');
    const img = document.getElementById('previewImg');
    const name = document.getElementById('previewName');

    if (file) {
        const reader = new FileReader();
        reader.onload = e => { img.src = e.target.result; };
        reader.readAsDataURL(file);
        name.textContent = file.name;
        wrap.style.display = 'block';
        // Yeni görsel seçilince "Kaldır" bayrağını sıfırla
        document.getElementById('removeImageFlag').value = 'false';
    } else {
        wrap.style.display = 'none';
    }
});

// ─── Form gönderimi (FormData — multipart) ────────────────────────────────────
document.getElementById('editForm')?.addEventListener('submit', async e => {
    e.preventDefault();
    const btn = e.submitter;
    if (btn) { btn.disabled = true; btn.textContent = '⏳ Güncelleniyor…'; }

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

    const formData = new FormData();
    formData.append('id', document.getElementById('menuItemId').value);
    formData.append('menuItemName', document.getElementById('menuItemName').value.trim());
    formData.append('categoryId', document.getElementById('categoryId').value);
    formData.append('menuItemPriceStr', document.getElementById('menuItemPrice').value);
    formData.append('description', document.getElementById('description').value.trim());
    formData.append('detailedDescription', document.getElementById('detailedDescription').value.trim());
    formData.append('stockQuantity', document.getElementById('stockQuantity').value);
    formData.append('trackStock', document.getElementById('trackStock').checked ? 'true' : 'false');
    formData.append('isAvailable', document.getElementById('isAvailable').checked ? 'true' : 'false');
    formData.append('removeImage', document.getElementById('removeImageFlag').value);

    const imageInput = document.getElementById('imageFile');
    if (imageInput?.files[0]) {
        formData.append('imageFile', imageInput.files[0]);
    }

    try {
        const res = await fetch(window.APP_URLS.menuEdit, {
            method: 'POST',
            headers: { 'RequestVerificationToken': token },
            body: formData
        });
        const data = await res.json();

        if (btn) { btn.disabled = false; btn.textContent = '💾 Güncelle'; }

        if (data.success) {
            window.location.href = window.APP_URLS.menuIndex;
        } else {
            const box = document.getElementById('alertBox');
            if (box) { box.textContent = data.message; box.style.display = 'block'; }
            else alert(data.message);
        }
    } catch (err) {
        if (btn) { btn.disabled = false; btn.textContent = '💾 Güncelle'; }
        const box = document.getElementById('alertBox');
        if (box) { box.textContent = 'Bağlantı hatası oluştu.'; box.style.display = 'block'; }
    }
});
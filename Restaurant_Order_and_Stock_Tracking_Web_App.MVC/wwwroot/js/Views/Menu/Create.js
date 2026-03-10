// ── Dil sekme geçişi ──────────────────────────────────────────
function switchLangTab(lang, btn) {
    document.querySelectorAll('.lang-tab').forEach(b => b.classList.remove('active'));
    document.querySelectorAll('.lang-panel').forEach(p => p.style.display = 'none');
    btn.classList.add('active');
    document.getElementById('panel-' + lang).style.display = '';
}

// ── Form gönder ───────────────────────────────────────────────
document.getElementById('createForm')?.addEventListener('submit', async e => {
    e.preventDefault();
    const btn = e.submitter;
    btn.disabled = true;

    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

    const payload = {
        menuItemName: document.getElementById('c_name').value.trim(),
        nameEn: document.getElementById('c_nameEn').value.trim(),
        nameAr: document.getElementById('c_nameAr').value.trim(),
        nameRu: document.getElementById('c_nameRu').value.trim(),
        categoryId: parseInt(document.getElementById('c_categoryId').value) || 0,
        menuItemPriceStr: document.getElementById('c_price').value,
        description: document.getElementById('c_description').value.trim(),
        descriptionEn: document.getElementById('c_descriptionEn').value.trim(),
        descriptionAr: document.getElementById('c_descriptionAr').value.trim(),
        descriptionRu: document.getElementById('c_descriptionRu').value.trim(),
        stockQuantity: parseInt(document.getElementById('c_stock').value) || 0,
        trackStock: document.getElementById('c_trackStock').checked,
        isAvailable: document.getElementById('c_isAvailable').checked
    };

    try {
        const res = await fetch(window.APP_URLS.menuCreate, {
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
// ─── Görsel önizleme ──────────────────────────────────────────────────────────
document.getElementById('c_imageFile')?.addEventListener('change', function () {
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
    } else {
        wrap.style.display = 'none';
    }
});

// ─── Form gönderimi (FormData — multipart) ────────────────────────────────────
document.getElementById('createForm')?.addEventListener('submit', async e => {
    e.preventDefault();
    const btn = e.submitter;
    if (btn) { btn.disabled = true; btn.textContent = '⏳ Kaydediliyor…'; }

    const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';

    // FormData; hem metin hem dosyayı taşır (enctype="multipart/form-data" gerekli)
    const formData = new FormData();
    formData.append('menuItemName', document.getElementById('c_name').value.trim());
    formData.append('categoryId', document.getElementById('c_categoryId').value);
    formData.append('menuItemPriceStr', document.getElementById('c_price').value);
    formData.append('description', document.getElementById('c_description').value.trim());
    formData.append('detailedDescription', document.getElementById('c_detailedDescription').value.trim());
    formData.append('stockQuantity', document.getElementById('c_stock').value);
    formData.append('trackStock', document.getElementById('c_trackStock').checked ? 'true' : 'false');
    formData.append('isAvailable', document.getElementById('c_isAvailable').checked ? 'true' : 'false');

    const imageInput = document.getElementById('c_imageFile');
    if (imageInput?.files[0]) {
        formData.append('imageFile', imageInput.files[0]);
    }

    try {
        const res = await fetch(window.APP_URLS.menuCreate, {
            method: 'POST',
            headers: { 'RequestVerificationToken': token },
            // Content-Type header'ı EKLEME — tarayıcı boundary'yi kendisi ekler
            body: formData
        });
        const data = await res.json();

        if (btn) { btn.disabled = false; btn.textContent = '💾 Kaydet'; }

        if (data.success) {
            window.location.href = window.APP_URLS.menuIndex;
        } else {
            const box = document.getElementById('alertBox');
            if (box) { box.textContent = data.message; box.style.display = 'block'; }
            else alert(data.message);
        }
    } catch (err) {
        if (btn) { btn.disabled = false; btn.textContent = '💾 Kaydet'; }
        const box = document.getElementById('alertBox');
        if (box) { box.textContent = 'Bağlantı hatası oluştu.'; box.style.display = 'block'; }
    }
});
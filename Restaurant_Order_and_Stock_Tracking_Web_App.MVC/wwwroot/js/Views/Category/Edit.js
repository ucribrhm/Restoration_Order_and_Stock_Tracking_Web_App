// ── Dil sekme geçişi ──────────────────────────────────────────
function switchLangTab(lang, btn) {
    document.querySelectorAll('.lang-tab').forEach(b => b.classList.remove('active'));
    document.querySelectorAll('.lang-panel').forEach(p => p.style.display = 'none');
    btn.classList.add('active');
    document.getElementById('panel-' + lang).style.display = '';
}

// ── Form gönder ───────────────────────────────────────────────
document.getElementById('editForm').addEventListener('submit', async e => {
    e.preventDefault();
    const btn = e.submitter;
    btn.disabled = true;

    const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

    const payload = {
        id: parseInt(document.getElementById('editId').value),
        categoryName: document.getElementById('categoryName').value.trim(),
        nameEn: document.getElementById('nameEn').value.trim(),
        nameAr: document.getElementById('nameAr').value.trim(),
        nameRu: document.getElementById('nameRu').value.trim(),
        categorySortOrder: parseInt(document.getElementById('sortOrder').value) || 0,
        isActive: document.getElementById('isActive').checked
    };

    try {
        const res = await fetch(window.APP_URLS.categoryEdit, {
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
            window.location.href = window.APP_URLS.categoryIndex;
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
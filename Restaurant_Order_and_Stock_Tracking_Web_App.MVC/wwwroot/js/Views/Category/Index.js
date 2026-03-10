document.addEventListener("DOMContentLoaded", () => {

    // ── Tab Switcher ─────────────────────────────────────────────────────
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

        modal.querySelectorAll('.lang-pane').forEach(pane => {
            pane.style.display = 'none';
        });

        const targetPane = modal.querySelector('#' + clickedBtn.getAttribute('data-target'));
        if (targetPane) targetPane.style.display = '';
    };

    // ── Modal Aç / Kapat ─────────────────────────────────────────────────
    window.openModal = function (id) {
        document.getElementById(id).classList.add('open');
    };

    window.closeModal = function (id) {
        document.getElementById(id).classList.remove('open');
    };

    document.querySelectorAll('.modal-overlay').forEach(overlay => {
        overlay.addEventListener('click', e => {
            if (e.target === overlay) overlay.classList.remove('open');
        });
    });

    // ── Toast ─────────────────────────────────────────────────────────────
    function showToast(message, type = 'success') {
        const container = document.getElementById('toastContainer');
        if (!container) return;
        const toast = document.createElement('div');
        toast.className = `toast toast-${type}`;
        toast.innerHTML = `<span>${type === 'success' ? '✅' : '❌'}</span><span>${message}</span>`;
        container.appendChild(toast);
        setTimeout(() => toast.remove(), 3500);
    }

    function getToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    // ── Sekmeleri TR'ye sıfırla ──────────────────────────────────────────
    function resetTabsToTR(modalId, tablistId, panePrefix) {
        const modal = document.getElementById(modalId);
        const tablist = document.getElementById(tablistId);
        if (!modal || !tablist) return;

        tablist.querySelectorAll('.lang-tab-btn').forEach(btn => {
            btn.classList.remove('active');
            btn.setAttribute('aria-selected', 'false');
        });

        const firstBtn = tablist.querySelector('.lang-tab-btn');
        if (firstBtn) {
            firstBtn.classList.add('active');
            firstBtn.setAttribute('aria-selected', 'true');
        }

        modal.querySelectorAll('.lang-pane').forEach(p => p.style.display = 'none');

        const trPane = modal.querySelector(`#${panePrefix}-pane-tr`);
        if (trPane) trPane.style.display = '';
    }

    // ── CREATE ───────────────────────────────────────────────────────────
    window.openCreateModal = function () {
        document.getElementById('createForm').reset();
        document.getElementById('create_isActive').checked = true;
        resetTabsToTR('createModal', 'createLangTabs', 'c');
        openModal('createModal');
    };

    document.getElementById('createForm')?.addEventListener('submit', async e => {
        e.preventDefault();
        const btn = e.submitter;
        btn.disabled = true;

        const payload = {
            categoryName: document.getElementById('create_categoryName').value.trim(),
            nameEn: document.getElementById('create_nameEn').value.trim() || null,
            nameAr: document.getElementById('create_nameAr').value.trim() || null,
            nameRu: document.getElementById('create_nameRu').value.trim() || null,
            categorySortOrder: parseInt(document.getElementById('create_sortOrder').value) || 0,
            isActive: document.getElementById('create_isActive').checked
        };

        try {
            const res = await fetch(window.APP_URLS.categoryCreate, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getToken() },
                body: JSON.stringify(payload)
            });
            const data = await res.json();
            btn.disabled = false;

            if (data.success) {
                closeModal('createModal');
                showToast(data.message, 'success');
                setTimeout(() => location.reload(), 800);
            } else {
                showToast(data.message, 'error');
            }
        } catch {
            btn.disabled = false;
            showToast('İşlem sırasında bir hata oluştu.', 'error');
        }
    });

    // ── EDIT ─────────────────────────────────────────────────────────────
    window.openEditModal = async function (id) {
        try {
            const res = await fetch(`${window.APP_URLS.categoryGetById}/${id}`);
            const data = await res.json();
            if (!data.success) { showToast('Veri alınamadı.', 'error'); return; }

            document.getElementById('edit_id').value = data.categoryId;
            document.getElementById('edit_categoryName').value = data.categoryName ?? '';
            document.getElementById('edit_nameEn').value = data.nameEn ?? '';
            document.getElementById('edit_nameAr').value = data.nameAr ?? '';
            document.getElementById('edit_nameRu').value = data.nameRu ?? '';
            document.getElementById('edit_sortOrder').value = data.categorySortOrder;
            document.getElementById('edit_isActive').checked = data.isActive;

            resetTabsToTR('editModal', 'editLangTabs', 'e');
            openModal('editModal');
        } catch {
            showToast('Veri çekilirken hata oluştu.', 'error');
        }
    };

    document.getElementById('editForm')?.addEventListener('submit', async e => {
        e.preventDefault();
        const btn = e.submitter;
        btn.disabled = true;

        const payload = {
            id: parseInt(document.getElementById('edit_id').value),
            categoryName: document.getElementById('edit_categoryName').value.trim(),
            nameEn: document.getElementById('edit_nameEn').value.trim() || null,
            nameAr: document.getElementById('edit_nameAr').value.trim() || null,
            nameRu: document.getElementById('edit_nameRu').value.trim() || null,
            categorySortOrder: parseInt(document.getElementById('edit_sortOrder').value) || 0,
            isActive: document.getElementById('edit_isActive').checked
        };

        try {
            const res = await fetch(window.APP_URLS.categoryEdit, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': getToken() },
                body: JSON.stringify(payload)
            });
            const data = await res.json();
            btn.disabled = false;

            if (data.success) {
                closeModal('editModal');
                showToast(data.message, 'success');
                setTimeout(() => location.reload(), 800);
            } else {
                showToast(data.message, 'error');
            }
        } catch {
            btn.disabled = false;
            showToast('İşlem sırasında bir hata oluştu.', 'error');
        }
    });

    // ── DELETE ───────────────────────────────────────────────────────────
    window.openDeleteModal = function (id, name) {
        document.getElementById('delete_id').value = id;
        document.getElementById('delete_name').textContent = name;
        openModal('deleteModal');
    };

    document.getElementById('deleteForm')?.addEventListener('submit', async e => {
        e.preventDefault();
        const btn = e.submitter;
        btn.disabled = true;

        const body = new URLSearchParams({
            id: document.getElementById('delete_id').value,
            __RequestVerificationToken: getToken()
        });

        try {
            const res = await fetch(window.APP_URLS.categoryDelete, { method: 'POST', body });
            const data = await res.json();
            btn.disabled = false;

            if (data.success) {
                closeModal('deleteModal');
                showToast(data.message, 'success');
                setTimeout(() => location.reload(), 800);
            } else {
                closeModal('deleteModal');
                showToast(data.message, 'error');
            }
        } catch {
            btn.disabled = false;
            closeModal('deleteModal');
            showToast('İşlem sırasında bir hata oluştu.', 'error');
        }
    });

});
document.addEventListener("DOMContentLoaded", () => {

    // ── 1. C# Verilerini HTML'den (JSON Adacığından) Oku ──
    const configEl = document.getElementById('salesReportConfig');
    let state = { preset: 'today', from: '', to: '', cancelled: false };

    if (configEl) {
        state = JSON.parse(configEl.textContent);
    }

    // ── Yardımcılar ──────────────────────────────────────────────────────────────
    function isDark() { return document.documentElement.dataset.theme !== 'light'; }
    function gridColor() { return isDark() ? 'rgba(255,255,255,.06)' : 'rgba(0,0,0,.06)'; }
    function textColor() { return isDark() ? '#687080' : '#8A95A3'; }

    let charts = {};

    function destroyChart(id) {
        if (charts[id]) {
            charts[id].destroy();
            delete charts[id];
        }
    }

    // ── Filtre durumu & URL Yönetimi ─────────────────────────────────────────────
    function buildQs() {
        const q = new URLSearchParams({ preset: state.preset, includeCancelled: state.cancelled });
        if (state.preset === 'custom') {
            q.set('from', state.from);
            q.set('to', state.to);
        }
        return q.toString();
    }

    function applyFilter() {
        history.replaceState(null, '', `/App/Reports/Sales?${buildQs()}`);
        loadAllCharts();
    }

    // ── Olay Dinleyicileri (Event Listeners) ─────────────────────────────────────

    // Preset butonları
    document.querySelectorAll('.preset-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            document.querySelectorAll('.preset-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            state.preset = btn.dataset.preset;

            const isCustom = state.preset === 'custom';
            const dateFromEl = document.getElementById('dateFrom');
            const dateToEl = document.getElementById('dateTo');

            if (dateFromEl) dateFromEl.style.display = isCustom ? '' : 'none';
            if (dateToEl) dateToEl.style.display = isCustom ? '' : 'none';

            if (!isCustom) applyFilter();
        });
    });

    const dateFromEl = document.getElementById('dateFrom');
    if (dateFromEl) {
        dateFromEl.addEventListener('change', e => { state.from = e.target.value; applyFilter(); });
    }

    const dateToEl = document.getElementById('dateTo');
    if (dateToEl) {
        dateToEl.addEventListener('change', e => { state.to = e.target.value; applyFilter(); });
    }

    const chkCancelled = document.getElementById('chkCancelled');
    if (chkCancelled) {
        chkCancelled.addEventListener('change', e => { state.cancelled = e.target.checked; applyFilter(); });
    }

    // Export butonları
    const btnCsv = document.getElementById('btnCsv');
    if (btnCsv) {
        btnCsv.addEventListener('click', () => {
            window.location = `/App/Reports/ExportCsv?type=sales&${buildQs()}`;
        });
    }

    const btnPdf = document.getElementById('btnPdf');
    if (btnPdf) {
        btnPdf.addEventListener('click', () => {
            window.location = `/App/Reports/ExportPdf?type=sales&${buildQs()}`;
        });
    }

    // ── Grafikleri yükle ─────────────────────────────────────────────────────────
    async function loadAllCharts() {
        await Promise.all([loadTrend(), loadPayment(), loadProducts(), loadCategory()]);
    }

    // ── Para birimi formatlama ────────────────────────────────────────────────────
    function formatCurrency(val) {
        return '₺' + Number(val).toLocaleString('tr-TR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    }

    // ── Widget güncelleme (FIX: filtre değiştiğinde 4 kart statik kalıyordu) ────
    function updateSummaryCards(data) {
        const gross = document.getElementById('sv-gross');
        const net = document.getElementById('sv-net');
        // Sprint 1: "Fark" kartı kaldırıldı → "Toplam İndirim" geldi
        const discount = document.getElementById('sv-discount');
        const count = document.getElementById('sv-count');

        if (gross) gross.textContent = formatCurrency(data.totalGross ?? 0);
        if (net) net.textContent = formatCurrency(data.totalCollected ?? 0);
        if (discount) discount.textContent = formatCurrency(data.totalDiscount ?? 0);
        if (count) count.textContent = data.orderCount ?? 0;
    }

    async function loadTrend() {
        const loadingEl = document.getElementById('trendLoading');
        if (loadingEl) loadingEl.style.display = 'flex';

        try {
            const res = await fetch(`/App/Reports/GetSalesChartData?${buildQs()}`);
            const data = await res.json();
            if (loadingEl) loadingEl.style.display = 'none';

            // ── FIX: Widget'ları yeni tarih aralığının toplamlarıyla güncelle ──
            updateSummaryCards(data);

            destroyChart('trend');
            const canvas = document.getElementById('trendChart');
            if (!canvas) return;

            const ctx = canvas.getContext('2d');
            charts['trend'] = new Chart(ctx, {
                type: 'line',
                data: {
                    labels: data.labels,
                    datasets: [
                        {
                            label: 'Brüt Ciro',
                            data: data.grossData,
                            borderColor: '#F97316',
                            backgroundColor: '#F97316',
                            borderWidth: 2.5,
                            tension: .35,
                            fill: false,
                            pointRadius: 3,
                            pointBackgroundColor: '#F97316'
                        },
                        {
                            label: 'Tahsilat',
                            data: data.netData,
                            borderColor: '#10b981',
                            backgroundColor: '#10b981',
                            borderWidth: 2.5,
                            borderDash: [6, 3],
                            tension: .35,
                            fill: false,
                            pointRadius: 4,
                            pointStyle: 'rect',
                            pointBackgroundColor: '#10b981'
                        }
                    ]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { labels: { color: textColor() } }
                    },
                    scales: {
                        x: { grid: { color: gridColor() }, ticks: { color: textColor(), maxRotation: 45 } },
                        y: { grid: { color: gridColor() }, ticks: { color: textColor(), callback: v => `₺${v}` } }
                    }
                }
            });
        } catch (e) {
            console.error("Trend grafiği yüklenirken hata oluştu:", e);
            if (loadingEl) loadingEl.style.display = 'none';
        }
    }

    async function loadPayment() {
        const loadingEl = document.getElementById('paymentLoading');
        if (loadingEl) loadingEl.style.display = 'flex';

        try {
            const res = await fetch(`/App/Reports/GetPaymentChartData?${buildQs()}`);
            const data = await res.json();
            if (loadingEl) loadingEl.style.display = 'none';

            destroyChart('payment');
            const canvas = document.getElementById('paymentChart');
            if (!canvas) return;

            const ctx = canvas.getContext('2d');
            charts['payment'] = new Chart(ctx, {
                type: 'doughnut',
                data: {
                    labels: data.labels,
                    datasets: [{
                        data: data.amounts,
                        backgroundColor: ['#F97316', '#3b82f6', '#10b981', '#8b5cf6'],
                        borderWidth: 2,
                        borderColor: isDark() ? '#161A20' : '#ffffff'
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { labels: { color: textColor() } },
                        tooltip: {
                            callbacks: {
                                label: ctx => `₺${ctx.parsed.toLocaleString('tr-TR', { minimumFractionDigits: 2 })} (%${data.percentages[ctx.dataIndex]})`
                            }
                        }
                    }
                }
            });
        } catch (e) {
            console.error("Ödeme grafiği yüklenirken hata oluştu:", e);
            if (loadingEl) loadingEl.style.display = 'none';
        }
    }

    async function loadProducts() {
        const loadingEl = document.getElementById('productsLoading');
        if (loadingEl) loadingEl.style.display = 'flex';

        try {
            const res = await fetch(`/App/Reports/GetTopProductsData?${buildQs()}&top=10`);
            const data = await res.json();
            if (loadingEl) loadingEl.style.display = 'none';

            destroyChart('products');
            const canvas = document.getElementById('productsChart');
            if (!canvas) return;

            const ctx = canvas.getContext('2d');
            charts['products'] = new Chart(ctx, {
                type: 'bar',
                data: {
                    labels: data.labels,
                    datasets: [{
                        label: 'Adet',
                        data: data.quantities,
                        backgroundColor: 'rgba(249,115,22,.75)',
                        borderColor: '#F97316',
                        borderWidth: 1,
                        borderRadius: 4
                    }]
                },
                options: {
                    indexAxis: 'y',
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: { legend: { display: false } },
                    scales: {
                        x: { grid: { color: gridColor() }, ticks: { color: textColor() } },
                        y: { grid: { display: false }, ticks: { color: textColor() } }
                    }
                }
            });
        } catch (e) {
            console.error("Ürünler grafiği yüklenirken hata oluştu:", e);
            if (loadingEl) loadingEl.style.display = 'none';
        }
    }

    async function loadCategory() {
        const loadingEl = document.getElementById('categoryLoading');
        if (loadingEl) loadingEl.style.display = 'flex';

        try {
            const res = await fetch(`/App/Reports/GetCategorySalesData?${buildQs()}`);
            const data = await res.json();
            if (loadingEl) loadingEl.style.display = 'none';

            destroyChart('category');
            const canvas = document.getElementById('categoryChart');
            if (!canvas) return;

            const ctx = canvas.getContext('2d');
            charts['category'] = new Chart(ctx, {
                type: 'pie',
                data: {
                    labels: data.labels,
                    datasets: [{
                        data: data.amounts,
                        backgroundColor: ['#F97316', '#3b82f6', '#10b981', '#8b5cf6', '#f59e0b', '#ef4444'],
                        borderWidth: 2,
                        borderColor: isDark() ? '#161A20' : '#ffffff'
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: { labels: { color: textColor() } },
                        tooltip: {
                            callbacks: {
                                label: ctx => `₺${ctx.parsed.toLocaleString('tr-TR', { minimumFractionDigits: 2 })} (%${data.percentages[ctx.dataIndex]})`
                            }
                        }
                    }
                }
            });
        } catch (e) {
            console.error("Kategori grafiği yüklenirken hata oluştu:", e);
            if (loadingEl) loadingEl.style.display = 'none';
        }
    }

    // ── İlk yükleme ──────────────────────────────────────────────────────────────
    loadAllCharts();

});
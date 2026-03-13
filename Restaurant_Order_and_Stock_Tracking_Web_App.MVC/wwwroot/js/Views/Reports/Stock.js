document.addEventListener("DOMContentLoaded", () => {

    // ── 1. C# Verilerini HTML'den (JSON Adacığından) Oku ──
    const configEl = document.getElementById('stockReportConfig');
    let state = { preset: 'today', from: '', to: '', tb: 'orderitem' };

    if (configEl) {
        state = JSON.parse(configEl.textContent);
    }

    let trendChart = null;

    // ── URL Parametreleri ve Yönlendirme ──
    function buildQs() {
        const q = new URLSearchParams({ preset: state.preset, timeBase: state.tb });
        if (state.preset === 'custom') {
            q.set('from', state.from);
            q.set('to', state.to);
        }
        const catFilterEl = document.getElementById('catFilter');
        if (catFilterEl && catFilterEl.value) {
            q.set('category', catFilterEl.value);
        }
        return q.toString();
    }

    function navigate() {
        window.location = `/App/Reports/Stock?${buildQs()}`;
    }

    // ── Olay Dinleyicileri (Event Listeners) ──

    // Preset (Zaman aralığı) Butonları
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

            if (!isCustom) navigate();
        });
    });

    // Timebase (Sipariş / Stok Hareketi) Butonları
    document.querySelectorAll('.timebase-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            document.querySelectorAll('.timebase-btn').forEach(b => b.classList.remove('active'));
            btn.classList.add('active');
            state.tb = btn.dataset.tb;
            navigate();
        });
    });

    // Tarih Seçiciler
    const dateFromEl = document.getElementById('dateFrom');
    if (dateFromEl) {
        dateFromEl.addEventListener('change', e => { state.from = e.target.value; navigate(); });
    }

    const dateToEl = document.getElementById('dateTo');
    if (dateToEl) {
        dateToEl.addEventListener('change', e => { state.to = e.target.value; navigate(); });
    }

    // Kategori Seçici
    const catFilterEl = document.getElementById('catFilter');
    if (catFilterEl) {
        catFilterEl.addEventListener('change', navigate);
    }

    // CSV İndirme
    const btnCsv = document.getElementById('btnCsv');
    if (btnCsv) {
        btnCsv.addEventListener('click', () => {
            window.location = `/App/Reports/ExportCsv?type=stock&${buildQs()}`;
        });
    }

    // ── Grafik Yardımcı Fonksiyonları ──
    function isDark() { return document.documentElement.dataset.theme !== 'light'; }
    function gridColor() { return isDark() ? 'rgba(255,255,255,.06)' : 'rgba(0,0,0,.06)'; }
    function textColor() { return isDark() ? '#687080' : '#8A95A3'; }

    // ── Trend Modal İşlemleri ──
    window.openTrendModal = async function (row) {
        const id = row.getAttribute('data-id');
        const name = row.getAttribute('data-name');

        document.getElementById('modalTitle').textContent = `📈 ${name} — Stok Trendi (Son 30 Gün)`;
        document.getElementById('trendModal').classList.add('open');
        document.getElementById('trendLoading').style.display = 'flex';

        if (trendChart) {
            trendChart.destroy();
            trendChart = null;
        }

        try {
            const res = await fetch(`/App/Reports/GetStockTrendData?menuItemId=${id}&days=30`);
            const data = await res.json();

            document.getElementById('trendLoading').style.display = 'none';

            const ctx = document.getElementById('trendChart').getContext('2d');
            trendChart = new Chart(ctx, {
                type: 'line',
                data: {
                    labels: data.labels,
                    datasets: [{
                        label: 'Stok',
                        data: data.stocks,
                        borderColor: '#F97316',
                        backgroundColor: '#F97316',
                        borderWidth: 2.5,
                        tension: .35,
                        fill: false,
                        pointRadius: 4,
                        pointBackgroundColor: '#F97316'
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: { legend: { display: false } },
                    scales: {
                        x: { grid: { color: gridColor() }, ticks: { color: textColor() } },
                        y: { grid: { color: gridColor() }, ticks: { color: textColor() }, beginAtZero: true }
                    }
                }
            });
        } catch (error) {
            console.error("Grafik verisi yüklenemedi:", error);
            document.getElementById('trendLoading').style.display = 'none';
        }
    };

    window.closeModal = function () {
        document.getElementById('trendModal').classList.remove('open');
    };

    // Modal Dışına Tıklayınca Kapatma
    const trendModalEl = document.getElementById('trendModal');
    if (trendModalEl) {
        trendModalEl.addEventListener('click', e => {
            if (e.target === trendModalEl) window.closeModal();
        });
    }

});
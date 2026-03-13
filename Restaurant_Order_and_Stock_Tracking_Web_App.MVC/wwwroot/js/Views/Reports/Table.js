document.addEventListener("DOMContentLoaded", () => {

    // ── 1. Filtre Konfigürasyonunu (JSON Adacığından) Oku ──
    const configEl = document.getElementById('tableReportConfig');
    let state = { preset: 'today', from: '', to: '' };

    if (configEl) {
        state = JSON.parse(configEl.textContent);
    }

    // ── 2. URL Parametreleri ve Yönlendirme ──
    function buildQs() {
        const q = new URLSearchParams({ preset: state.preset });
        if (state.preset === 'custom') {
            q.set('from', state.from);
            q.set('to', state.to);
        }
        return q.toString();
    }

    function navigate() {
        window.location = `/App/Reports/Table?${buildQs()}`;
    }

    // ── 3. Olay Dinleyicileri (Event Listeners) ──

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

    // Tarih Seçiciler
    const dateFromEl = document.getElementById('dateFrom');
    if (dateFromEl) {
        dateFromEl.addEventListener('change', e => { state.from = e.target.value; navigate(); });
    }

    const dateToEl = document.getElementById('dateTo');
    if (dateToEl) {
        dateToEl.addEventListener('change', e => { state.to = e.target.value; navigate(); });
    }

    // CSV İndirme
    const btnCsv = document.getElementById('btnCsv');
    if (btnCsv) {
        btnCsv.addEventListener('click', () => {
            window.location = `/App/Reports/ExportCsv?type=table&${buildQs()}`;
        });
    }

    // ── 4. Grafik (Chart) İşlemleri ──
    function isDark() { return document.documentElement.dataset.theme !== 'light'; }
    function gridColor() { return isDark() ? 'rgba(255,255,255,.06)' : 'rgba(0,0,0,.06)'; }
    function textColor() { return isDark() ? '#687080' : '#8A95A3'; }

    const loadingEl = document.getElementById('chartLoading');
    if (loadingEl) loadingEl.style.display = 'none';

    // Grafik verisini JSON adacığından oku
    const chartDataEl = document.getElementById('tableChartData');
    let tableData = [];
    if (chartDataEl) {
        tableData = JSON.parse(chartDataEl.textContent);
    }

    // Veri varsa grafiği çiz
    if (tableData.length > 0) {
        const canvas = document.getElementById('tableRevenueChart');
        if (canvas) {
            const ctx = canvas.getContext('2d');
            new Chart(ctx, {
                type: 'bar',
                data: {
                    labels: tableData.map(x => x.name),
                    datasets: [{
                        label: 'Ciro (₺)',
                        data: tableData.map(x => x.revenue),
                        backgroundColor: 'rgba(249,115,22,.75)',
                        borderColor: '#F97316',
                        borderWidth: 2,
                        borderRadius: 6
                    }]
                },
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: { legend: { display: false } },
                    scales: {
                        x: { grid: { color: gridColor() }, ticks: { color: textColor() } },
                        y: { grid: { color: gridColor() }, ticks: { color: textColor(), callback: v => `₺${v}` } }
                    }
                }
            });
        }
    }

});
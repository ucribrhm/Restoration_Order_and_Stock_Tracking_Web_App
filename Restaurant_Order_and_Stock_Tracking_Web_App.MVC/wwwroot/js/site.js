// ════════════════════════════════════════════════════════════════════════════
//  wwwroot/js/site.js
//
//  SPRINT A — [SA-1] Global fetch() Wrapper ve Session Modal
//
//  window.safeFetch(url, options?)
//    Tüm AJAX/fetch çağrıları doğrudan fetch() yerine bu fonksiyonu kullanır.
//    Otomatik olarak X-Requested-With ve Content-Type headerları ekler.
//    Response 401 → showSessionExpiredModal() tetiklenir, null döner.
//    Response başarılı → response nesnesi döner (caller .json() çağırır).
//
//  showSessionExpiredModal(loginUrl)
//    Ekranın ortasında overlay + kart şeklinde oturum uyarısı gösterir.
//    Kullanıcı "Giriş Yap" butonuna tıklarsa loginUrl'e yönlendirilir.
//    Modal bir kez oluşur; tekrar çağrılsa ikincisi eklenmez.
// ════════════════════════════════════════════════════════════════════════════

// ── [SA-1] Global Fetch Wrapper ─────────────────────────────────────────────
//
//  Kullanım (tüm JS dosyalarında fetch() yerine):
//    const res = await window.safeFetch('/App/Orders/AddItemBulk', {
//        method: 'POST',
//        body: JSON.stringify(payload)
//    });
//    if (!res) return;          // 401 → modal gösterildi, işlemi durdur
//    const data = await res.json();
//
window.safeFetch = async function (url, options = {}) {
    try {
        const mergedOptions = {
            ...options,
            headers: {
                // AJAX isteği olduğunu sunucuya bildir →
                // Program.cs OnRedirectToLogin 302 yerine 401 döner
                'X-Requested-With': 'XMLHttpRequest',
                'Content-Type': 'application/json',
                // Çağıran taraftan gelen header'lar üzerine yazar
                ...(options.headers || {})
            }
        };

        const response = await fetch(url, mergedOptions);

        // ── 401: Oturum sona erdi ──────────────────────────────────────
        if (response.status === 401) {
            let redirectUrl = '/App/Auth/Login'; // güvenli varsayılan
            try {
                const data = await response.json();
                if (data?.redirectUrl) redirectUrl = data.redirectUrl;
            } catch {
                // JSON parse edilemedi — varsayılan yönlendirme kullanılır
            }
            showSessionExpiredModal(redirectUrl);
            return null; // caller null kontrolü yapmalı
        }

        return response;

    } catch (err) {
        // Ağ hatası veya başka bir istisna
        console.error('[safeFetch] İstek başarısız:', url, err);
        return null;
    }
};

// ── [SA-1] Session Expired Modal ────────────────────────────────────────────
//
//  Ekranın ortasında sabit overlay ile oturum sona erme uyarısı gösterir.
//  Modal zaten açıksa ikincisi eklenmez (idempotent).
//
window.showSessionExpiredModal = function (loginUrl) {
    // Zaten açık modal varsa tekrar ekleme
    if (document.getElementById('session-expired-overlay')) return;

    const overlay = document.createElement('div');
    overlay.id = 'session-expired-overlay';
    overlay.style.cssText = [
        'position:fixed',
        'inset:0',
        'background:rgba(0,0,0,0.75)',
        'display:flex',
        'align-items:center',
        'justify-content:center',
        'z-index:99999',
        'backdrop-filter:blur(4px)',
        '-webkit-backdrop-filter:blur(4px)',
        'animation:fadeIn .2s ease'
    ].join(';');

    overlay.innerHTML = `
        <style>
            @keyframes fadeIn  { from { opacity:0 } to { opacity:1 } }
            @keyframes slideUp { from { transform:translateY(16px);opacity:0 }
                                 to   { transform:translateY(0);opacity:1 } }
            #session-expired-card {
                background: var(--surface, #1a1d27);
                border: 1px solid var(--accent, #f97316);
                border-radius: 16px;
                padding: 2.2rem 2rem;
                text-align: center;
                max-width: 380px;
                width: 90%;
                box-shadow: 0 25px 60px rgba(0,0,0,.5);
                animation: slideUp .25s ease;
                font-family: 'DM Sans', sans-serif;
            }
            #session-expired-card .sei-icon {
                font-size: 2.4rem;
                margin-bottom: .8rem;
            }
            #session-expired-card .sei-title {
                font-size: 1.1rem;
                font-weight: 700;
                color: var(--text, #e2e8f0);
                margin-bottom: .5rem;
                font-family: 'Syne', 'DM Sans', sans-serif;
            }
            #session-expired-card .sei-body {
                font-size: .9rem;
                color: var(--text-muted, #94a3b8);
                margin-bottom: 1.6rem;
                line-height: 1.5;
            }
            #session-expired-card .sei-btn {
                display: inline-block;
                padding: .7rem 2rem;
                background: var(--accent, #f97316);
                color: #fff;
                font-size: .95rem;
                font-weight: 600;
                border-radius: 10px;
                text-decoration: none;
                transition: background .2s, transform .1s;
                cursor: pointer;
                border: none;
                font-family: inherit;
            }
            #session-expired-card .sei-btn:hover  { background: #ea6c0a; }
            #session-expired-card .sei-btn:active { transform: scale(.97); }
        </style>
        <div id="session-expired-card">
            <div class="sei-icon">🔒</div>
            <div class="sei-title">Oturumunuz Sona Erdi</div>
            <div class="sei-body">
                Güvenliğiniz için oturumunuz otomatik olarak kapatıldı.<br>
                Devam etmek için lütfen tekrar giriş yapın.
            </div>
            <a href="${loginUrl}" class="sei-btn">🚪 Giriş Yap</a>
        </div>
    `;

    document.body.appendChild(overlay);
};


// ════════════════════════════════════════════════════════════════════════════
//  Sayfa UI mantığı (değişmedi)
// ════════════════════════════════════════════════════════════════════════════
document.addEventListener("DOMContentLoaded", () => {

    // ── Sidebar Collapse (Aç/Kapa) Mantığı ──
    const sidebar = document.getElementById('sidebar');
    const mainEl = document.getElementById('main');
    const toggleBtn = document.getElementById('toggleBtn');

    if (toggleBtn && sidebar && mainEl) {
        toggleBtn.addEventListener('click', () => {
            const collapsed = sidebar.classList.toggle('collapsed');
            mainEl.classList.toggle('shifted', collapsed);
            localStorage.setItem('sidebarCollapsed', collapsed);
        });

        // Sayfa yüklendiğinde önceki durumu kontrol et
        if (localStorage.getItem('sidebarCollapsed') === 'true') {
            sidebar.classList.add('collapsed');
            mainEl.classList.add('shifted');
        }
    }

    // ── Dark / Light Tema Değiştirme Mantığı ──
    const html = document.documentElement;
    const themeToggle = document.getElementById('themeToggle');

    // Sayfa ilk yüklendiğinde varsayılan temayı uygula
    html.dataset.theme = localStorage.getItem('theme') || 'dark';

    if (themeToggle) {
        themeToggle.addEventListener('click', () => {
            const next = html.dataset.theme === 'dark' ? 'light' : 'dark';
            html.dataset.theme = next;
            localStorage.setItem('theme', next);
        });
    }

    // ── Topbar Canlı Saat ──
    const clockEl = document.getElementById('clock');
    if (clockEl) {
        function tick() {
            clockEl.textContent = new Date().toLocaleTimeString('tr-TR', {
                hour: '2-digit',
                minute: '2-digit'
            });
        }
        setInterval(tick, 1000);
        tick(); // Sayfa açılır açılmaz saati göster
    }

});
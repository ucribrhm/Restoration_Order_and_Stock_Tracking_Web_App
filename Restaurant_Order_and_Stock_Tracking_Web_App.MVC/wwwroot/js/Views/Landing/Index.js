/**
 * wwwroot/js/Views/Landing/Index.js
 * RestaurantOS — Landing Page JavaScript
 *
 * İçerik:
 *   1. Tema Toggle (dark / light, localStorage)
 *   2. Navbar scroll efekti (frosted glass)
 *   3. Hamburger / Mobile menu
 *   4. Sayaç animasyonu (Intersection Observer)
 *   5. Scroll reveal kartlar (Intersection Observer)
 */

(function () {
    'use strict';

    /* ════════════════════════════════════════════════════════
       1. TEMA TOGGLE
       ════════════════════════════════════════════════════════ */

    var THEME_KEY = 'ros-theme';
    var html = document.documentElement;
    var themeBtn = document.getElementById('themeBtn');

    /* Mevcut temayı oku, ikonu güncelle */
    function syncThemeIcon() {
        if (!themeBtn) return;
        var t = html.getAttribute('data-theme') || 'dark';
        themeBtn.textContent = t === 'dark' ? '🌙' : '☀️';
        themeBtn.setAttribute('aria-label', t === 'dark' ? 'Açık temaya geç' : 'Koyu temaya geç');
    }

    /* Tema değiştir */
    function toggleTheme() {
        if (!themeBtn) return;
        var current = html.getAttribute('data-theme') || 'dark';
        var next = current === 'dark' ? 'light' : 'dark';

        html.setAttribute('data-theme', next);
        try { localStorage.setItem(THEME_KEY, next); } catch (e) { /* private mode */ }

        /* İkon döndürme animasyonu */
        themeBtn.classList.remove('spinning');
        void themeBtn.offsetWidth; /* reflow — animasyonu sıfırla */
        themeBtn.classList.add('spinning');
        themeBtn.addEventListener('animationend', function () {
            themeBtn.classList.remove('spinning');
            syncThemeIcon();
        }, { once: true });
    }

    if (themeBtn) {
        syncThemeIcon();
        themeBtn.addEventListener('click', toggleTheme);
    }

    /* ════════════════════════════════════════════════════════
       2. NAVBAR SCROLL EFEKTİ
       ════════════════════════════════════════════════════════ */

    var navbar = document.getElementById('navbar');
    var lastScrollY = 0;

    function onScroll() {
        if (!navbar) return;
        var y = window.scrollY;

        /* Frosted glass: scroll > 60px */
        if (y > 60) {
            navbar.classList.add('scrolled');
        } else {
            navbar.classList.remove('scrolled');
        }

        lastScrollY = y;
    }

    /* Throttle: her 80ms'de bir çalıştır */
    var scrollTimer = null;
    window.addEventListener('scroll', function () {
        if (scrollTimer) return;
        scrollTimer = setTimeout(function () {
            onScroll();
            scrollTimer = null;
        }, 80);
    }, { passive: true });

    onScroll(); /* Sayfa yüklenince ilk çalıştır */

    /* ════════════════════════════════════════════════════════
       3. HAMBURGER / MOBILE MENU
       ════════════════════════════════════════════════════════ */

    var hamburger = document.getElementById('hamburger');
    var mobileMenu = document.getElementById('mobileMenu');
    var mobileMenuClose = document.getElementById('mobileMenuClose');

    function openMobileMenu() {
        if (!mobileMenu || !hamburger) return;
        mobileMenu.classList.add('open');
        hamburger.classList.add('open');
        hamburger.setAttribute('aria-expanded', 'true');
        document.body.style.overflow = 'hidden';
    }

    function closeMobileMenu() {
        if (!mobileMenu || !hamburger) return;
        mobileMenu.classList.remove('open');
        hamburger.classList.remove('open');
        hamburger.setAttribute('aria-expanded', 'false');
        document.body.style.overflow = '';
    }

    if (hamburger) hamburger.addEventListener('click', function () {
        var isOpen = mobileMenu && mobileMenu.classList.contains('open');
        isOpen ? closeMobileMenu() : openMobileMenu();
    });

    if (mobileMenuClose) mobileMenuClose.addEventListener('click', closeMobileMenu);

    /* ESC tuşu ile kapat */
    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') closeMobileMenu();
    });

    /* Overlay dışına tıklayınca kapat */
    if (mobileMenu) mobileMenu.addEventListener('click', function (e) {
        if (e.target === mobileMenu) closeMobileMenu();
    });

    /* Menü linke tıklayınca kapat */
    if (mobileMenu) {
        var mobileLinks = mobileMenu.querySelectorAll('a');
        mobileLinks.forEach(function (link) {
            link.addEventListener('click', closeMobileMenu);
        });
    }

    /* ════════════════════════════════════════════════════════
       4. SAYAÇ ANİMASYONU
       ════════════════════════════════════════════════════════ */

    var counters = document.querySelectorAll('.stat-number[data-target]');
    var countersStarted = false;

    /**
     * Sayıyı binlik gruplarla formatla: 50000 → "50.000"
     */
    function formatGrouped(n) {
        return n.toString().replace(/\B(?=(\d{3})+(?!\d))/g, '.');
    }

    /**
     * Tek bir sayacı anime et: 0'dan target'a 1500ms'de
     */
    function animateCounter(el) {
        var target = parseInt(el.getAttribute('data-target'), 10);
        var suffix = el.getAttribute('data-suffix') || '';
        var grouped = el.hasAttribute('data-format') && el.getAttribute('data-format') === 'grouped';
        var duration = 1500; /* ms */
        var startTime = null;

        function step(timestamp) {
            if (!startTime) startTime = timestamp;
            var progress = Math.min((timestamp - startTime) / duration, 1);

            /* Easing: easeOutQuart */
            var eased = 1 - Math.pow(1 - progress, 4);
            var current = Math.round(eased * target);

            var display = grouped ? formatGrouped(current) : current.toString();
            el.textContent = display + suffix;

            if (progress < 1) {
                requestAnimationFrame(step);
            }
        }

        requestAnimationFrame(step);
    }

    function startCounters() {
        if (countersStarted) return;
        countersStarted = true;
        counters.forEach(function (el) {
            animateCounter(el);
        });
    }

    /* Intersection Observer: sayaçlar ekrana girince başlat */
    if (counters.length > 0) {
        var statsSection = document.getElementById('stats');
        if (statsSection) {
            var statsObserver = new IntersectionObserver(function (entries) {
                entries.forEach(function (entry) {
                    if (entry.isIntersecting) {
                        startCounters();
                        statsObserver.disconnect();
                    }
                });
            }, { threshold: 0.3 });

            statsObserver.observe(statsSection);
        } else {
            /* stats bölümü bulunamazsa 1sn sonra başlat */
            setTimeout(startCounters, 1000);
        }
    }

    /* ════════════════════════════════════════════════════════
       5. SCROLL REVEAL — Özellik Kartları
       ════════════════════════════════════════════════════════ */

    var revealEls = document.querySelectorAll('.scroll-reveal');

    if (revealEls.length > 0 && 'IntersectionObserver' in window) {
        var revealObserver = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    entry.target.classList.add('revealed');
                    revealObserver.unobserve(entry.target); /* Bir kez tetikle */
                }
            });
        }, {
            threshold: 0.12,
            rootMargin: '0px 0px -40px 0px'
        });

        revealEls.forEach(function (el) {
            revealObserver.observe(el);
        });
    } else {
        /* IntersectionObserver yoksa tümünü göster */
        revealEls.forEach(function (el) {
            el.classList.add('revealed');
        });
    }

    /* ════════════════════════════════════════════════════════
       6. SMOOTH ANCHOR SCROLL (nav linkleri için)
       ════════════════════════════════════════════════════════ */

    document.querySelectorAll('a[href^="#"]').forEach(function (anchor) {
        anchor.addEventListener('click', function (e) {
            var href = this.getAttribute('href');
            if (href === '#') return;
            var target = document.querySelector(href);
            if (!target) return;
            e.preventDefault();

            var navH = navbar ? navbar.offsetHeight : 0;
            var targetY = target.getBoundingClientRect().top + window.scrollY - navH - 12;

            window.scrollTo({ top: targetY, behavior: 'smooth' });
        });
    });

})();
// ============================================================================
//  Models/TenantConfig.cs
//  SaaS Çok Kiracılı Yapı — Kiracı Özellik Bayrakları (Feature Flags)
//
//  Tenant ile 1-1 ilişkili. Her kiracının hangi özellikleri kullanacağını,
//  vergi oranını, para birimini ve marka ayarlarını saklar.
//
//  Tenant oluşturulduğunda TenantConfigSeeder tarafından RestaurantType'a
//  göre varsayılan değerlerle otomatik doldurulur.
//  Admin panelinden kiracı başına değiştirilebilir.
// ============================================================================
namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models
{
    public class TenantConfig
    {
        // ── PK / FK ─────────────────────────────────────────────────────────
        /// <summary>Tenant.TenantId ile 1-1 ilişki. Aynı zamanda PK.</summary>
        public string TenantId { get; set; } = string.Empty;

        public virtual Tenant Tenant { get; set; } = null!;

        // ════════════════════════════════════════════════════════════════════
        //  OPERASYONEL ÖZELLİK BAYRAKLARI
        //  true  → özellik aktif
        //  false → özellik gizli / kilitli
        // ════════════════════════════════════════════════════════════════════

        // ── Masa & Sipariş Akışı ────────────────────────────────────────────
        /// <summary>
        /// İki masanın adisyonunu tek adisyona birleştirme.
        /// Önerilen: CasualDining, FineDining, Franchise → true
        /// </summary>
        public bool EnableTableMerge { get; set; } = false;

        /// <summary>
        /// Mutfak Ekran Sistemi (KDS). Tüm tipler için önerilen.
        /// </summary>
        public bool EnableKitchenDisplay { get; set; } = true;

        /// <summary>
        /// Müşterinin QR kodu okutarak kendi siparişini vermesi.
        /// Önerilen: FastFood, CasualDining → true
        /// </summary>
        public bool EnableSelfOrderQr { get; set; } = false;

        /// <summary>
        /// Fine Dining kurs yönetimi: Başlangıç → Ana Yemek → Tatlı tur sistemi.
        /// Önerilen: FineDining, Franchise → true
        /// </summary>
        public bool EnableCourseManagement { get; set; } = false;

        /// <summary>Masa rezervasyon modülü.</summary>
        public bool EnableReservations { get; set; } = true;

        // ── Ödeme & Finans ──────────────────────────────────────────────────
        /// <summary>
        /// Misafir bazlı hesap bölüşümü (herkes kendi yediğini öder).
        /// Önerilen: CasualDining, FineDining → true
        /// </summary>
        public bool EnableSplitBill { get; set; } = false;

        /// <summary>Kasiyer/yönetici indirim uygulayabilir mi?</summary>
        public bool EnableDiscounts { get; set; } = true;

        /// <summary>
        /// KDV oranı yüzde olarak. Örn: 10.00 = %10.
        /// null = KDV hesabı yapılmıyor, fişe yazdırılmıyor.
        /// </summary>
        public decimal? TaxRate { get; set; } = null;

        /// <summary>ISO 4217 para birimi. Varsayılan: TRY</summary>
        public string CurrencyCode { get; set; } = "TRY";

        // ── CRM & Sadakat ───────────────────────────────────────────────────
        /// <summary>Müşteri kayıt ve sadakat puan sistemi.</summary>
        public bool EnableLoyaltyProgram { get; set; } = false;

        // ── Çok Şube (Franchise) ────────────────────────────────────────────
        /// <summary>
        /// Franchise merkezi birden fazla şubeyi yönetebilir.
        /// Yalnızca Franchise + Enterprise plan kombinasyonunda etkinleştirin.
        /// </summary>
        public bool EnableMultiBranch { get; set; } = false;

        // ── Görünüm & Marka ─────────────────────────────────────────────────
        /// <summary>QR menüde ve müşteri ekranlarında gösterilecek logo yolu.</summary>
        public string? LogoPath { get; set; }

        /// <summary>
        /// Marka ana rengi (hex). Örn: "#E63946".
        /// QR menü, müşteri ekranı ve termal fiş rengi için kullanılır.
        /// </summary>
        public string? PrimaryColor { get; set; }

        /// <summary>QR menüde görünecek restoran adı (Tenant.Name'den farklı olabilir).</summary>
        public string? RestaurantDisplayName { get; set; }
    }
}
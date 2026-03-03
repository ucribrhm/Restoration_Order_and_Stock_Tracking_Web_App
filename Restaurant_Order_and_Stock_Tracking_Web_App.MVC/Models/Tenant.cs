// ============================================================================
//  Models/Tenant.cs
//  SaaS Çok Kiracılı Yapı — Ana Kiracı (Restoran) Tanım Modeli
//
//  Her kayıtlı restoran bu tabloda bir satır olarak tutulur.
//  TenantId → slug formatında, insan okunabilir, URL-safe benzersiz kimlik.
//  Örn: "burger-palace-sisli"
//
//  EF Core konfigürasyonu: RestaurantDbContext.OnModelCreating() içinde
// ============================================================================
namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models
{
    /// <summary>
    /// Restoran tipini belirtir. Onboard sırasında seçilir.
    /// TenantConfig'in varsayılan feature flag'lerini belirler.
    /// </summary>
    public enum RestaurantType
    {
        FastFood = 0,
        CasualDining = 1,
        FineDining = 2,
        Franchise = 3
    }

    public class Tenant
    {
        // ── Kimlik ──────────────────────────────────────────────────────────
        /// <summary>
        /// Benzersiz kiracı kimliği. Slug formatında, küçük harf, tire ile ayrılmış.
        /// PK olarak kullanılır (string PK → varchar(100)).
        /// Örn: "burger-palace-sisli"
        /// </summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Restoranın tam ticari adı.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Alt alan adı. Müşteri bu subdomain üzerinden sisteme erişir.
        /// Örn: "burgerpalace" → burgerpalace.restaurantos.com
        /// Benzersiz olmalı (DbContext'te unique index tanımlı).
        /// </summary>
        public string Subdomain { get; set; } = string.Empty;

        // ── Abonelik ────────────────────────────────────────────────────────
        /// <summary>Abonelik planı: "trial" | "starter" | "pro" | "enterprise"</summary>
        public string PlanType { get; set; } = "trial";

        /// <summary>Kiracı aktif mi? false ise giriş engellenir.</summary>
        public bool IsActive { get; set; } = true;

        // ── Tarihler ────────────────────────────────────────────────────────
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Deneme süresi bitiş tarihi.
        /// null → ücretli plan, deneme süresi yok.
        /// Dolduğunda giriş kısıtlanır (AuthController'da kontrol edilir).
        /// </summary>
        public DateTime? TrialEndsAt { get; set; }

        // ── Tip ─────────────────────────────────────────────────────────────
        public RestaurantType RestaurantType { get; set; } = RestaurantType.CasualDining;

        // ── İlişkiler ───────────────────────────────────────────────────────
        /// <summary>1-1 ilişki. Tenant oluşturulurken otomatik seed edilir.</summary>
        public virtual TenantConfig? Config { get; set; }

        public virtual ICollection<ApplicationUser> Users { get; set; } = [];
    }
}
// ============================================================================
//  Models/MenuItem.cs
//  DEĞİŞİKLİK — FAZ 1 ADIM 2: Multi-Tenancy
//
//  EKLENEN: TenantId (zorunlu string, FK → tenants.TenantId)
//  EF Core Global Query Filter bu alan üzerinden izolasyonu sağlar.
//  DİĞER TÜM ALANLAR AYNEN KORUNDU.
// ============================================================================
namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models
{
    public class MenuItem
    {
        public int MenuItemId { get; set; }
        public int CategoryId { get; set; }
        public virtual Category Category { get; set; }

        // ── FAZ 1 ADIM 2: Multi-Tenancy ─────────────────────────────────────
        /// <summary>
        /// Bu menü ürününün ait olduğu restoranın TenantId'si.
        /// FK → tenants.TenantId
        /// EF Core Global Query Filter bu sütunu kullanarak tenant izolasyonunu
        /// otomatik olarak uygular — geliştirici Where() yazmayı unutsa bile.
        ///
        /// NOT: StockLog bu tablo üzerinden JOIN ile izole edilir;
        /// StockLog'a ayrıca TenantId eklenmez (performans optimizasyonu).
        /// </summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Navigasyon. İsteğe bağlı kullanım.</summary>
        public virtual Tenant? Tenant { get; set; }
        // ─────────────────────────────────────────────────────────────────────

        // ── Ürün Adı (çok dilli) ─────────────────────────────────────────────
        public string MenuItemName { get; set; }   // TR (zorunlu)
        public string? NameEn { get; set; }         // EN
        public string? NameAr { get; set; }         // AR
        public string? NameRu { get; set; }         // RU

        public decimal MenuItemPrice { get; set; }
        public decimal? CostPrice { get; set; } = null;

        public int AlertThreshold { get; set; } = 0;
        public int CriticalThreshold { get; set; } = 0;
        public int StockQuantity { get; set; }
        public bool TrackStock { get; set; }
        public bool IsAvailable { get; set; }
        public bool IsDeleted { get; set; } = false;

        // ── Kısa Açıklama (çok dilli) ────────────────────────────────────────
        public string? Description { get; set; }     // TR (mevcut)
        public string? DescriptionEn { get; set; }   // EN
        public string? DescriptionAr { get; set; }   // AR
        public string? DescriptionRu { get; set; }   // RU

        /// <summary>QR Menü detay sayfasında gösterilen uzun açıklama.</summary>
        public string? DetailedDescription { get; set; }

        /// <summary>wwwroot göreli yolu, ör: /images/menu/abc123.jpg</summary>
        public string? ImagePath { get; set; }

        public DateTime MenuItemCreatedTime { get; set; }
    }
}
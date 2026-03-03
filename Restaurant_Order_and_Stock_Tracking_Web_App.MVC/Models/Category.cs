// ============================================================================
//  Models/Category.cs
//  DEĞİŞİKLİK — FAZ 1 ADIM 2: Multi-Tenancy
//
//  EKLENEN: TenantId (zorunlu string, FK → tenants.TenantId)
//  EF Core Global Query Filter bu alan üzerinden izolasyonu sağlar.
//  DİĞER TÜM ALANLAR AYNEN KORUNDU.
//
//  ÖNEMLİ: Orijinal kod'daki CategoryName unique index'i KALDIRILDI.
//  Sebep: Farklı restoranlar aynı kategori adını kullanabilir (ör: "Çorbalar").
//  Yeni unique kısıtı: (TenantId + CategoryName) — DbContext'te tanımlı.
// ============================================================================
namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;

public class Category
{
    public int CategoryId { get; set; }

    // ── FAZ 1 ADIM 2: Multi-Tenancy ─────────────────────────────────────────
    /// <summary>
    /// Bu kategorinin ait olduğu restoranın TenantId'si.
    /// FK → tenants.TenantId
    /// EF Core Global Query Filter bu sütunu kullanarak tenant izolasyonunu
    /// otomatik olarak uygular — geliştirici Where() yazmayı unutsa bile.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Navigasyon. İsteğe bağlı kullanım.</summary>
    public virtual Tenant? Tenant { get; set; }
    // ─────────────────────────────────────────────────────────────────────────

    public string CategoryName { get; set; }       // TR (mevcut)
    public string? NameEn { get; set; }             // EN
    public string? NameAr { get; set; }             // AR
    public string? NameRu { get; set; }             // RU
    public int CategorySortOrder { get; set; }
    public bool IsActive { get; set; }

    public virtual ICollection<MenuItem> MenuItems { get; set; }
}
// ============================================================================
//  Models/Order.cs
//  DEĞİŞİKLİK — FAZ 1 ADIM 2: Multi-Tenancy
//
//  EKLENEN: TenantId (zorunlu string, FK → tenants.TenantId)
//  EF Core Global Query Filter bu alan üzerinden izolasyonu sağlar.
//  DİĞER TÜM ALANLAR AYNEN KORUNDU.
// ============================================================================
namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;

public class Order
{
    public int OrderId { get; set; }
    public int TableId { get; set; }
    public virtual Table Table { get; set; }

    // ── FAZ 1 ADIM 2: Multi-Tenancy ─────────────────────────────────────────
    /// <summary>
    /// Bu adisyonun ait olduğu restoranın TenantId'si.
    /// FK → tenants.TenantId
    /// EF Core Global Query Filter bu sütunu kullanarak tenant izolasyonunu
    /// otomatik olarak uygular — geliştirici Where() yazmayı unutsa bile.
    ///
    /// NOT: OrderItem ve Payment bu tablo üzerinden JOIN ile izole edilir;
    /// o tablolara ayrıca TenantId eklenmez (performans optimizasyonu).
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Navigasyon. İsteğe bağlı kullanım.</summary>
    public virtual Tenant? Tenant { get; set; }
    // ─────────────────────────────────────────────────────────────────────────

    public string OrderStatus { get; set; }
    public string OrderOpenedBy { get; set; }
    public string? OrderNote { get; set; }
    public decimal OrderTotalAmount { get; set; }
    public DateTime OrderOpenedAt { get; set; }
    public DateTime? OrderClosedAt { get; set; }
    public virtual ICollection<OrderItem> OrderItems { get; set; }
    public virtual ICollection<Payment> Payments { get; set; }
}
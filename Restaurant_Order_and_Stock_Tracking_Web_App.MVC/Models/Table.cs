// ============================================================================
//  Models/Table.cs
//  DEĞİŞİKLİK — FAZ 1 ADIM 2: Multi-Tenancy
//
//  EKLENEN: TenantId (zorunlu string, FK → tenants.TenantId)
//  EF Core Global Query Filter bu alan üzerinden izolasyonu sağlar.
//  DİĞER TÜM ALANLAR AYNEN KORUNDU.
// ============================================================================
namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models
{
    public class Table
    {
        public int TableId { get; set; }

        // ── FAZ 1 ADIM 2: Multi-Tenancy ─────────────────────────────────────
        /// <summary>
        /// Bu masanın ait olduğu restoranın TenantId'si.
        /// FK → tenants.TenantId
        /// EF Core Global Query Filter bu sütunu kullanarak tenant izolasyonunu
        /// otomatik olarak uygular — geliştirici Where() yazmayı unutsa bile.
        /// </summary>
        public string TenantId { get; set; } = string.Empty;

        /// <summary>Navigasyon. İsteğe bağlı kullanım.</summary>
        public virtual Tenant? Tenant { get; set; }
        // ─────────────────────────────────────────────────────────────────────

        public string TableName { get; set; }
        public int TableCapacity { get; set; }
        public int TableStatus { get; set; }
        public DateTime TableCreatedAt { get; set; }

        public string? ReservationName { get; set; }
        public string? ReservationPhone { get; set; }
        public int? ReservationGuestCount { get; set; }
        public DateTime? ReservationTime { get; set; }

        /// <summary>
        /// Müşteri "Garson Çağır"a bastığında true olur.
        /// Garson "İlgilenildi" dediğinde false'a döner.
        /// </summary>
        public bool IsWaiterCalled { get; set; } = false;

        /// <summary>
        /// Garson çağrısının başladığı UTC zaman damgası.
        /// DismissWaiter çağrıldığında null'a döner.
        /// SLA takibi (10 dk ihlal) için kullanılır.
        /// </summary>
        public DateTime? WaiterCalledAt { get; set; }

        public virtual ICollection<Order> Orders { get; set; }
    }
}
// ============================================================================
//  Models/Order.cs
//  DEĞİŞİKLİK — FAZ 1 FİNAL: String → Enum Geçişi
//
//  [ENUM-1] OrderStatus tipi: string → OrderStatus enum
//           DB'de hâlâ "open"/"paid"/"cancelled" olarak saklanır
//           (Value Converter — bkz. RestaurantDbContext.cs)
//
//  KORUNAN: TenantId, Tenant navigation, diğer tüm alanlar
// ============================================================================
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared.Common;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;

public class Order
{
    public int OrderId { get; set; }
    public int TableId { get; set; }
    public virtual Table Table { get; set; } = null!;

    // ── Multi-Tenancy ─────────────────────────────────────────────────────────
    public string TenantId { get; set; } = string.Empty;
    public virtual Tenant? Tenant { get; set; }

    // ── [ENUM-1] String → Enum ────────────────────────────────────────────────
    // C# tarafı: OrderStatus enum (type-safe, IntelliSense desteği)
    // DB tarafı : "open" / "paid" / "cancelled"  (JS frontend uyumlu)
    // Dönüşüm   : RestaurantDbContext.OnModelCreating → Value Converter
    public OrderStatus OrderStatus { get; set; } = OrderStatus.Open;

    public string? OrderOpenedBy { get; set; }
    public string? OrderNote { get; set; }
    public decimal OrderTotalAmount { get; set; }
    public DateTime OrderOpenedAt { get; set; }
    public DateTime? OrderClosedAt { get; set; }

    public virtual ICollection<OrderItem> OrderItems { get; set; } = new List<OrderItem>();
    public virtual ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
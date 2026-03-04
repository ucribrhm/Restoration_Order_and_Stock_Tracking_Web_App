// ============================================================================
//  Models/OrderItem.cs
//  DEĞİŞİKLİK — FAZ 1 FİNAL: String → Enum Geçişi
//
//  [ENUM-2] OrderItemStatus tipi: string → OrderItemStatus enum
//           DB'de hâlâ "pending"/"preparing"/"ready"/"served"/"cancelled"
//           (Value Converter — bkz. RestaurantDbContext.cs)
//
//  KORUNAN: İptal/iade alanları, hesaplanan özellikler, diğer tüm alanlar
// ============================================================================
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared.Common;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;

public class OrderItem
{
    public int OrderItemId { get; set; }
    public int OrderId { get; set; }
    public virtual Order Order { get; set; } = null!;
    public int MenuItemId { get; set; }
    public virtual MenuItem MenuItem { get; set; } = null!;

    /// <summary>Sipariş edilen toplam adet</summary>
    public int OrderItemQuantity { get; set; }

    /// <summary>Bu kalem için ödenmiş adet</summary>
    public int PaidQuantity { get; set; } = 0;

    // ── İPTAL / İADE ALANLARI ─────────────────────────────────────────────────

    /// <summary>
    /// Kısmi veya tam iptal edilen adet.
    /// OrderItemQuantity sabit kalır; sadece bu alan artar.
    /// ActiveQuantity = OrderItemQuantity - CancelledQuantity
    /// </summary>
    public int CancelledQuantity { get; set; } = 0;

    /// <summary>İptal sebebi (garson girer, opsiyonel)</summary>
    public string? CancelReason { get; set; }

    /// <summary>
    /// Yalnızca TrackStock=true olan ürünler için dolu olur.
    ///  true  → Zayi / Fire (ürün kullanıldı, stoka iade edilmez)
    ///  false → Kullanılmadı (stoka iade edilir)
    ///  null  → Stok takibi olmayan ürün, stok işlemi yapılmadı
    /// </summary>
    public bool? IsWasted { get; set; }

    // ── FİYAT / DURUM ─────────────────────────────────────────────────────────

    public decimal OrderItemUnitPrice { get; set; }

    /// <summary>
    /// Aktif (iptal edilmemiş) adet üzerinden hesaplanan tutar.
    /// İptal sonrası güncellenir.
    /// </summary>
    public decimal OrderItemLineTotal { get; set; }

    public string? OrderItemNote { get; set; }

    // ── [ENUM-2] String → Enum ─────────────────────────────────────────────────
    // C# tarafı: OrderItemStatus enum (type-safe)
    // DB tarafı : "pending" / "preparing" / "ready" / "served" / "cancelled"
    // KDS JS    : string karşılaştırmalar DB'den gelen string'le çalışır
    // Dönüşüm   : RestaurantDbContext.OnModelCreating → Value Converter
    public OrderItemStatus OrderItemStatus { get; set; } = OrderItemStatus.Pending;

    public DateTime OrderItemAddedAt { get; set; }

    // ── Hesaplanan özellikler — DB'ye yazılmaz ─────────────────────────────────

    /// <summary>İptal edilmemiş aktif adet</summary>
    public int ActiveQuantity => OrderItemQuantity - CancelledQuantity;

    /// <summary>Ödenmemiş ve iptal edilmemiş adet</summary>
    public int RemainingQuantity => ActiveQuantity - PaidQuantity;

    /// <summary>Ödenmemiş kısım tutarı</summary>
    public decimal UnpaidLineTotal => RemainingQuantity * OrderItemUnitPrice;

    /// <summary>Ödenmiş kısım tutarı</summary>
    public decimal PaidLineTotal => PaidQuantity * OrderItemUnitPrice;

    /// <summary>İptal edilen kısım tutarı</summary>
    public decimal CancelledLineTotal => CancelledQuantity * OrderItemUnitPrice;
}
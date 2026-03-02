namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Dtos.Orders;

public class OrderPaymentDto
{
    public int OrderId { get; set; }
    public string? PayerName { get; set; }
    public string PaymentMethod { get; set; } = "cash";
    public decimal PaymentAmount { get; set; }

    // ── İndirim alanları (yeni) ─────────────────────────────────────────
    /// <summary>
    /// "amount"  → sabit TL tutarı (örn: 15 → ₺15 indirim)
    /// "percent" → yüzdelik       (örn: 10 → %10 indirim)
    /// </summary>
    public string DiscountType { get; set; } = "amount";

    /// <summary>
    /// DiscountType = "amount"  → bu değer doğrudan TL tutarıdır.
    /// DiscountType = "percent" → bu değer yüzdedir (0–100).
    /// Backend bu değerden gerçek TL indirimini hesaplar.
    /// </summary>
    public decimal DiscountValue { get; set; } = 0;

    // Geriye dönük uyumluluk — artık backend tarafından hesaplanır,
    // frontend'den göndermek zorunda değilsiniz.
    public decimal DiscountAmount { get; set; } = 0;

    public List<PaidItemSelectionDto>? PaidItems { get; set; }
}

public class PaidItemSelectionDto
{
    public int OrderItemId { get; set; }
    public int Quantity { get; set; }
}
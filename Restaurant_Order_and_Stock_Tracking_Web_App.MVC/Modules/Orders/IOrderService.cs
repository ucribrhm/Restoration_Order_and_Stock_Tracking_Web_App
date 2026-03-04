// ============================================================================
//  Modules/Orders/IOrderService.cs
//  FAZ 1 FİNAL — Servis Katmanı (Modüler Monolith)
//
//  NEDEN SERVIS KATMANI?
//  ─────────────────────
//  Şimdiye kadar tüm iş mantığı (stok kontrolü, SignalR tetikleme,
//  transaction yönetimi) OrdersController içindeydi. Bu yaklaşım:
//    - Test edilmesini imkânsız kılıyor (Controller HTTP'ye bağımlı)
//    - İleride mikroservise taşımayı zorlaştırıyor
//    - Controller'ı 700+ satıra şişiriyor
//
//  BU ARABIRIM:
//    - Tüm yazma operasyonlarını tanımlar
//    - OrdersController artık sadece bu interface'i çağırır (Thin Controller)
//    - Gelecekte OrderService → Ayrı mikro servis olarak ayıklanabilir
//
//  DÖNEN TİPLER: ServiceResult<T> — generic, HTTP'den bağımsız sonuç
// ============================================================================
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Dtos.Orders;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Modules.Orders
{
    // ── Sonuç Tipleri ─────────────────────────────────────────────────────────

    /// <summary>
    /// HTTP'den bağımsız operasyon sonucu.
    /// Controller bu sonucu alıp JSON'a veya HTTP durum koduna çevirir.
    /// </summary>
    public record ServiceResult(bool Success, string Message);

    /// <summary>Veri taşıyan genel sonuç tipi.</summary>
    public record ServiceResult<T>(bool Success, string Message, T? Data = default)
        : ServiceResult(Success, Message);

    // Spesifik sonuç tipleri — her metot için anlamlı data
    public record CreateOrderResult(int OrderId, string TableName, decimal Total);
    public record AddItemResult(int OrderItemId, string MenuItemName);
    public record PaymentResult(bool IsClosed, decimal Remaining, decimal NetTotal);
    public record CancelItemResult(bool OrderAutoClose, bool TracksStock, bool IsWasted);

    // ── Interface ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Adisyon yönetimi iş mantığı sözleşmesi.
    /// Tüm operasyonlar tenant-aware'dir; TenantId constructor injection ile alınır.
    /// </summary>
    public interface IOrderService
    {
        /// <summary>
        /// Yeni adisyon açar, ürünleri ekler, stok düşer, masa durumunu günceller.
        /// SignalR: Dashboard'a "Adisyon Açıldı" bildirimi gönderir.
        /// </summary>
        Task<ServiceResult<CreateOrderResult>> CreateOrderAsync(OrderCreateDto dto, string openedBy);

        /// <summary>
        /// Açık adisyona tekil ürün ekler. Mevcut aynı kalemin üstüne yazar.
        /// SignalR: Dashboard + KDS (mutfak ekranı) bildirimi gönderir.
        /// </summary>
        Task<ServiceResult<AddItemResult>> AddItemAsync(OrderItemAddDto dto);

        /// <summary>
        /// Açık adisyona toplu ürün ekler.
        /// [PERF] N+1 çözümü: tüm MenuItemId'leri tek sorguyla çeker.
        /// SignalR: Dashboard + KDS bildirimi gönderir.
        /// </summary>
        Task<ServiceResult> AddItemBulkAsync(BulkAddDto dto);

        /// <summary>
        /// Sipariş kalemi durumunu günceller (pending→preparing→served vs.)
        /// NOT: Bu metot KDS'den de kullanılabilir; KitchenController doğrudan
        /// DB'ye yazmak yerine bu servisi kullanabilir (ileriki refactor).
        /// </summary>
        Task<ServiceResult> UpdateItemStatusAsync(OrderItemStatusUpdateDto dto);

        /// <summary>
        /// Kısmi veya tam ödeme kaydeder. Adisyon tamamen ödenince otomatik kapatır.
        /// SignalR: Kapanışta Dashboard'a bildirim.
        /// </summary>
        Task<ServiceResult<PaymentResult>> AddPaymentAsync(OrderPaymentDto dto);

        /// <summary>
        /// Tek seferlik tam ödeme ile adisyon kapatır.
        /// SignalR: Dashboard'a bildirim.
        /// </summary>
        Task<ServiceResult> CloseAsync(OrderCloseDto dto);

        /// <summary>
        /// Sıfır tutarlı adisyonu iptal ederek kapatır.
        /// Güvenlik: Aktif ürün kalmışsa reddeder.
        /// SignalR: Dashboard'a bildirim.
        /// </summary>
        Task<ServiceResult> CloseZeroAsync(OrderCloseZeroDto dto);

        /// <summary>
        /// Sipariş kalemi iptali. Stok takibi varsa zayi/iade kaydı oluşturur.
        /// Kalan tutar sıfırlanırsa adisyonu otomatik kapatır.
        /// </summary>
        Task<ServiceResult<CancelItemResult>> CancelItemAsync(OrderItemCancelDto dto);
    }
}
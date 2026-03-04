// ============================================================================
//  Modules/Orders/OrderService.cs
//  FAZ 1 FİNAL — Servis Katmanı Implementasyonu
//
//  TAŞINAN SORUMLULUKLAR (OrdersController'dan):
//  ──────────────────────────────────────────────
//  ✓ Stok kontrolü ve stok düşme mantığı
//  ✓ Transaction yönetimi (BeginTransaction / Commit / Rollback)
//  ✓ SignalR bildirimleri (Dashboard + KDS)
//  ✓ İptal/iade iş kuralları (PaidQuantity kontrolü vb.)
//  ✓ Ödeme hesaplamaları (indirim, net tutar, kalan)
//
//  [PERF] N+1 ÇÖZÜMÜ — AddItemBulkAsync:
//  ─────────────────────────────────────
//  ESKİ: foreach döngüsü içinde her ürün için ayrı FindAsync() → N DB isteği
//  YENİ: Tüm MenuItemId'leri topla → tek WHERE IN sorgusu → Dictionary'e al
//        İstekler = 1 (ürün sayısından bağımsız)
//
//  ENUM UYUMU:
//  ─────────────
//  OrderStatus ve OrderItemStatus artık enum. Tüm karşılaştırmalar enum ile.
//  DB'ye string olarak yazılır (Value Converter — RestaurantDbContext).
// ============================================================================
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Dtos.Orders;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared.Common;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Modules.Orders
{
    public class OrderService : IOrderService
    {
        private readonly RestaurantDbContext _db;
        private readonly ITenantService _tenantService;
        private readonly IHubContext<NotificationHub> _notifHub;   // Dashboard bildirimleri
        private readonly IHubContext<RestaurantHub> _kitchenHub;   // KDS (mutfak ekranı)

        public OrderService(
            RestaurantDbContext db,
            ITenantService tenantService,
            IHubContext<NotificationHub> notifHub,
            IHubContext<RestaurantHub> kitchenHub)
        {
            _db = db;
            _tenantService = tenantService;
            _notifHub = notifHub;
            _kitchenHub = kitchenHub;
        }

        // ── Özel Yardımcı: Dashboard SignalR Bildirimi ────────────────────────
        // Fire-and-forget; hub hatası operasyonu durdurmamalı.
        private Task NotifyDashboardAsync(string icon, string message, string color = "#f97316")
            => _notifHub.Clients
                .Group(_tenantService.TenantId ?? "")
                .SendAsync("ReceiveNotification", new
                {
                    icon,
                    message,
                    color,
                    time = DateTime.Now.ToString("HH:mm")
                });

        // ── Özel Yardımcı: KDS SignalR Bildirimi ─────────────────────────────
        private Task NotifyKitchenAsync(object payload)
            => _kitchenHub.Clients
                .Group(_tenantService.TenantId ?? "")
                .SendAsync("NewOrderItem", payload);

        // ═══════════════════════════════════════════════════════════════════════
        //  1. CreateOrderAsync — Adisyon Aç
        // ═══════════════════════════════════════════════════════════════════════
        public async Task<ServiceResult<CreateOrderResult>> CreateOrderAsync(
            OrderCreateDto dto, string openedBy)
        {
            if (dto.Items == null || !dto.Items.Any())
                return new(false, "En az bir ürün eklemelisiniz.");

            var table = await _db.Tables.FindAsync(dto.TableId);
            if (table == null)
                return new(false, "Masa bulunamadı.");

            // Stok ön kontrolü — döngüye girmeden önce hepsini kontrol et
            foreach (var line in dto.Items)
            {
                if (line.Quantity < 1) continue;
                var miCheck = await _db.MenuItems.FindAsync(line.MenuItemId);
                if (miCheck == null) continue;
                if (miCheck.TrackStock && miCheck.StockQuantity < line.Quantity)
                    return new(false,
                        $"Stok yetersiz: {miCheck.MenuItemName} — mevcut {miCheck.StockQuantity} adet",
                        new CreateOrderResult(0, table.TableName, 0));
            }

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var order = new Order
                {
                    TableId = dto.TableId,
                    OrderStatus = OrderStatus.Open,           // [ENUM]
                    OrderOpenedBy = openedBy,
                    OrderNote = string.IsNullOrWhiteSpace(dto.OrderNote) ? null : dto.OrderNote.Trim(),
                    OrderTotalAmount = 0,
                    OrderOpenedAt = DateTime.UtcNow,
                    TenantId = _tenantService.TenantId!
                };
                _db.Orders.Add(order);
                await _db.SaveChangesAsync();

                decimal total = 0;
                foreach (var line in dto.Items)
                {
                    var mi = await _db.MenuItems.FindAsync(line.MenuItemId);
                    if (mi == null) continue;
                    int qty = Math.Max(1, line.Quantity);
                    decimal lineTotal = mi.MenuItemPrice * qty;

                    _db.OrderItems.Add(new OrderItem
                    {
                        OrderId = order.OrderId,
                        MenuItemId = mi.MenuItemId,
                        OrderItemQuantity = qty,
                        PaidQuantity = 0,
                        OrderItemUnitPrice = mi.MenuItemPrice,
                        OrderItemLineTotal = lineTotal,
                        OrderItemNote = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim(),
                        OrderItemStatus = OrderItemStatus.Pending, // [ENUM]
                        OrderItemAddedAt = DateTime.UtcNow
                    });
                    total += lineTotal;

                    if (mi.TrackStock)
                    {
                        mi.StockQuantity -= qty;
                        if (mi.StockQuantity <= 0) { mi.StockQuantity = 0; mi.IsAvailable = false; }
                    }
                }

                order.OrderTotalAmount = total;
                table.TableStatus = 1;
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                // SignalR — Dashboard
                _ = NotifyDashboardAsync("🧾",
                    $"{table.TableName} için yeni adisyon açıldı — ₺{total:N0}", "#f97316");

                // SignalR — KDS: Tam adisyon açıldığında mutfak ekranını güncelle
                // NewOrderItem'dan farklı olarak tüm adisyonu temsil eder;
                // Kitchen Display bu event'i alarak kart grid'ini yeniler.
                _ = _kitchenHub.Clients
                    .Group(_tenantService.TenantId ?? "")
                    .SendAsync("NewOrder", new
                    {
                        orderId = order.OrderId,
                        tableName = table.TableName,
                        itemCount = dto.Items.Count,
                        total = total
                    });

                return new(true, "Adisyon açıldı.",
                    new CreateOrderResult(order.OrderId, table.TableName, total));
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return new(false, "Adisyon açılırken hata oluştu: " + ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  2. AddItemAsync — Tekil Ürün Ekle
        // ═══════════════════════════════════════════════════════════════════════
        public async Task<ServiceResult<AddItemResult>> AddItemAsync(OrderItemAddDto dto)
        {
            var order = await _db.Orders
                .Include(o => o.OrderItems)
                .Include(o => o.Table)
                .FirstOrDefaultAsync(o => o.OrderId == dto.OrderId);
            var mi = await _db.MenuItems.FindAsync(dto.MenuItemId);

            if (order == null || mi == null)
                return new(false, "Adisyon veya ürün bulunamadı.");
            if (order.OrderStatus != OrderStatus.Open)  // [ENUM]
                return new(false, "Kapalı adisyona ürün eklenemez.");

            if (dto.Quantity < 1) dto.Quantity = 1;

            if (mi.TrackStock && mi.StockQuantity < dto.Quantity)
                return new(false,
                    $"Stok yetersiz: mevcut {mi.StockQuantity} adet. ({mi.MenuItemName})");

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var noteNorm = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
                int savedItemId;

                // ── [SPRINT-2 FIX] State Transition Guard ──────────────────────────
                // SORUN: Mevcut satır Served/Preparing/Ready ise miktar artırımı
                //        geçersiz durum geçişine yol açıyor (Served → Preparing).
                // ÇÖZÜM: Sadece Pending + ödenmiş değil satırlara birleş;
                //        diğer durumlarda her zaman yeni Pending satır aç.
                var existing = order.OrderItems.FirstOrDefault(oi =>
                    oi.MenuItemId == dto.MenuItemId &&
                    oi.OrderItemStatus == OrderItemStatus.Pending && // [FIX] Sadece Pending'e birleş
                    oi.PaidQuantity < oi.OrderItemQuantity &&
                    oi.OrderItemNote == noteNorm);
                // ───────────────────────────────────────────────────────────────────

                if (existing != null)
                {
                    existing.OrderItemQuantity += dto.Quantity;
                    existing.OrderItemLineTotal = existing.OrderItemUnitPrice * existing.OrderItemQuantity;
                    savedItemId = existing.OrderItemId;
                }
                else
                {
                    var newItem = new OrderItem
                    {
                        OrderId = dto.OrderId,
                        MenuItemId = dto.MenuItemId,
                        OrderItemQuantity = dto.Quantity,
                        PaidQuantity = 0,
                        OrderItemUnitPrice = mi.MenuItemPrice,
                        OrderItemLineTotal = mi.MenuItemPrice * dto.Quantity,
                        OrderItemNote = noteNorm,
                        OrderItemStatus = OrderItemStatus.Pending, // [ENUM]
                        OrderItemAddedAt = DateTime.UtcNow
                    };
                    _db.OrderItems.Add(newItem);
                    await _db.SaveChangesAsync(); // Id için erken kayıt
                    savedItemId = newItem.OrderItemId;
                }

                order.OrderTotalAmount += mi.MenuItemPrice * dto.Quantity;

                if (mi.TrackStock)
                {
                    mi.StockQuantity -= dto.Quantity;
                    if (mi.StockQuantity <= 0) { mi.StockQuantity = 0; mi.IsAvailable = false; }
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                // SignalR — Dashboard
                _ = NotifyDashboardAsync("🍽️",
                    $"{order.Table?.TableName ?? $"#{order.OrderId}"} — {mi.MenuItemName} ×{dto.Quantity}",
                    "#f97316");

                // SignalR — KDS
                _ = NotifyKitchenAsync(new
                {
                    orderItemId = savedItemId,
                    orderId = order.OrderId,
                    tableName = order.Table?.TableName ?? $"#{order.OrderId}",
                    menuItemName = mi.MenuItemName,
                    quantity = dto.Quantity,
                    note = noteNorm,
                    addedAt = DateTime.UtcNow
                });

                return new(true, $"{mi.MenuItemName} eklendi.",
                    new AddItemResult(savedItemId, mi.MenuItemName));
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return new(false, "Ürün eklenirken hata oluştu: " + ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  3. AddItemBulkAsync — Toplu Ürün Ekle
        //  [PERF] N+1 Çözümü: Tek WHERE IN sorgusuyla tüm MenuItems belleğe alınır
        // ═══════════════════════════════════════════════════════════════════════
        public async Task<ServiceResult> AddItemBulkAsync(BulkAddDto dto)
        {
            if (dto.Items == null || !dto.Items.Any())
                return new(false, "Eklenecek ürün bulunamadı.");

            var order = await _db.Orders
                .Include(o => o.OrderItems)
                .Include(o => o.Table)
                .FirstOrDefaultAsync(o => o.OrderId == dto.OrderId);

            if (order == null)
                return new(false, "Adisyon bulunamadı.");
            if (order.OrderStatus != OrderStatus.Open)  // [ENUM]
                return new(false, "Kapalı adisyona ürün eklenemez.");

            // ── [PERF] N+1 ÇÖZÜMÜ ─────────────────────────────────────────────
            // ESKİ: foreach içinde her satır için FindAsync() → N adet SELECT
            // YENİ: Benzersiz ID'leri topla → tek SELECT WHERE IN → Dictionary
            var uniqueIds = dto.Items.Select(i => i.MenuItemId).Distinct().ToList();

            var menuItemsDict = await _db.MenuItems
                .Where(m => uniqueIds.Contains(m.MenuItemId))
                .ToDictionaryAsync(m => m.MenuItemId);
            // ── [PERF] Artık döngüde DB isteği yapılmıyor ─────────────────────

            // Stok ön kontrolü — transaction başlamadan hepsini kontrol et
            foreach (var line in dto.Items)
            {
                if (!menuItemsDict.TryGetValue(line.MenuItemId, out var miCheck)) continue;
                int qty = Math.Max(1, line.Quantity);
                if (miCheck.TrackStock && miCheck.StockQuantity < qty)
                    return new(false,
                        $"Stok yetersiz: {miCheck.MenuItemName} — mevcut {miCheck.StockQuantity} adet");
            }

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                decimal totalAdded = 0;
                var kdsPayloads = new List<object>();

                foreach (var line in dto.Items)
                {
                    if (!menuItemsDict.TryGetValue(line.MenuItemId, out var mi)) continue;
                    int qty = Math.Max(1, line.Quantity);
                    var noteNorm = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim();

                    // ── [SPRINT-2 FIX] State Transition Guard ──────────────────────
                    // SORUN: Served/Preparing/Ready satıra miktar eklenmesi sonraki
                    //        KDS geçişini Served→Preparing yaparak hata verir.
                    // ÇÖZÜM: Sadece Pending statüsündeki ve ödenmemiş satırlara birleş;
                    //        tüm diğer durumlar için yeni Pending satır oluştur.
                    var existing = order.OrderItems.FirstOrDefault(oi =>
                        oi.MenuItemId == line.MenuItemId &&
                        oi.OrderItemStatus == OrderItemStatus.Pending && // [FIX] Sadece Pending'e birleş
                        oi.CancelledQuantity == 0 &&
                        oi.PaidQuantity < oi.OrderItemQuantity &&
                        oi.OrderItemNote == noteNorm);
                    // ───────────────────────────────────────────────────────────────

                    int savedItemId;
                    if (existing != null)
                    {
                        existing.OrderItemQuantity += qty;
                        existing.OrderItemLineTotal = existing.OrderItemUnitPrice * existing.OrderItemQuantity;
                        savedItemId = existing.OrderItemId;
                    }
                    else
                    {
                        var newItem = new OrderItem
                        {
                            OrderId = dto.OrderId,
                            MenuItemId = line.MenuItemId,
                            OrderItemQuantity = qty,
                            PaidQuantity = 0,
                            CancelledQuantity = 0,
                            OrderItemUnitPrice = mi.MenuItemPrice,
                            OrderItemLineTotal = mi.MenuItemPrice * qty,
                            OrderItemNote = noteNorm,
                            OrderItemStatus = OrderItemStatus.Pending, // [ENUM]
                            OrderItemAddedAt = DateTime.UtcNow
                        };
                        _db.OrderItems.Add(newItem);
                        await _db.SaveChangesAsync();
                        savedItemId = newItem.OrderItemId;
                    }

                    totalAdded += mi.MenuItemPrice * qty;

                    if (mi.TrackStock)
                    {
                        mi.StockQuantity -= qty;
                        if (mi.StockQuantity <= 0) { mi.StockQuantity = 0; mi.IsAvailable = false; }
                    }

                    kdsPayloads.Add(new
                    {
                        orderItemId = savedItemId,
                        orderId = order.OrderId,
                        tableName = order.Table?.TableName ?? $"#{order.OrderId}",
                        menuItemName = mi.MenuItemName,
                        quantity = qty,
                        note = noteNorm,
                        addedAt = DateTime.UtcNow
                    });
                }

                order.OrderTotalAmount += totalAdded;
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                // SignalR — Dashboard
                _ = NotifyDashboardAsync("🛒",
                    $"{order.Table?.TableName ?? $"#{order.OrderId}"} — {dto.Items.Count} ürün eklendi (+₺{totalAdded:N0})",
                    "#f97316");

                // SignalR — KDS (her kalem için ayrı event)
                foreach (var payload in kdsPayloads)
                    _ = NotifyKitchenAsync(payload);

                return new(true, $"{dto.Items.Count} ürün eklendi.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return new(false, "Ürünler eklenirken hata oluştu: " + ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  4. UpdateItemStatusAsync — Kalem Durum Güncelle
        // ═══════════════════════════════════════════════════════════════════════
        public async Task<ServiceResult> UpdateItemStatusAsync(OrderItemStatusUpdateDto dto)
        {
            // String → Enum dönüşümü: DTO'dan gelen string'i enum'a çevir
            if (!Enum.TryParse<OrderItemStatus>(dto.NewStatus, ignoreCase: true, out var newStatus))
                return new(false, $"Geçersiz durum: '{dto.NewStatus}'");

            var item = await _db.OrderItems.FindAsync(dto.OrderItemId);
            if (item == null)
                return new(false, "Kalem bulunamadı.");

            item.OrderItemStatus = newStatus; // [ENUM]
            await _db.SaveChangesAsync();
            return new(true, "Durum güncellendi.");
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  5. AddPaymentAsync — Kısmi / Tam Ödeme
        // ═══════════════════════════════════════════════════════════════════════
        public async Task<ServiceResult<PaymentResult>> AddPaymentAsync(OrderPaymentDto dto)
        {
            if (dto.PaymentAmount <= 0)
                return new(false, "Geçerli bir ödeme tutarı giriniz.");
            if (dto.DiscountValue < 0)
                return new(false, "İndirim değeri negatif olamaz.");
            if (dto.DiscountType == "percent" && dto.DiscountValue > 100)
                return new(false, "Yüzde indirim 0–100 arasında olmalıdır.");

            var order = await _db.Orders
                .Include(o => o.Payments)
                .Include(o => o.Table)
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.OrderId == dto.OrderId);

            if (order == null)
                return new(false, "Adisyon bulunamadı.");
            if (order.OrderStatus != OrderStatus.Open)  // [ENUM]
                return new(false, "Bu adisyon zaten kapatılmış.");

            decimal discountAmount = dto.DiscountType == "percent"
                ? Math.Round(order.OrderTotalAmount * (dto.DiscountValue / 100m), 2)
                : Math.Round(dto.DiscountValue, 2);
            discountAmount = Math.Min(Math.Max(discountAmount, 0), order.OrderTotalAmount);

            decimal netTotal = order.OrderTotalAmount - discountAmount;
            decimal alreadyPaid = order.Payments.Sum(p => p.PaymentsAmount);
            decimal remaining = netTotal - alreadyPaid;

            if (dto.PaymentAmount > remaining + 0.01m)
                return new(false, $"Ödeme tutarı kalan tutarı (₺{remaining:N2}) aşamaz.");

            int methodCode = dto.PaymentMethod switch
            {
                "credit_card" => 1,
                "debit_card" => 2,
                "other" => 3,
                _ => 0
            };

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                _db.Payments.Add(new Payment
                {
                    OrderId = dto.OrderId,
                    PaymentsMethod = methodCode,
                    PaymentsAmount = dto.PaymentAmount,
                    PaymentsChangeGiven = 0,
                    PaymentsPaidAt = DateTime.UtcNow,
                    PaymentsNote = string.IsNullOrWhiteSpace(dto.PayerName) ? "" : dto.PayerName.Trim()
                });

                bool hasItemSel = dto.PaidItems != null && dto.PaidItems.Any();
                if (hasItemSel)
                {
                    foreach (var sel in dto.PaidItems!)
                    {
                        var oi = order.OrderItems.FirstOrDefault(x => x.OrderItemId == sel.OrderItemId);
                        if (oi == null || sel.Quantity <= 0) continue;
                        int canPay = oi.OrderItemQuantity - oi.PaidQuantity;
                        oi.PaidQuantity += Math.Min(sel.Quantity, canPay);
                    }
                }
                else
                {
                    decimal budget = dto.PaymentAmount;
                    var unpaid = order.OrderItems
                        .Where(oi => oi.OrderItemStatus != OrderItemStatus.Cancelled // [ENUM]
                                  && oi.PaidQuantity < oi.OrderItemQuantity)
                        .OrderBy(oi => oi.OrderItemAddedAt).ToList();
                    foreach (var oi in unpaid)
                    {
                        if (budget <= 0.001m) break;
                        int canAfford = (int)Math.Floor(budget / oi.OrderItemUnitPrice);
                        int payQty = Math.Min(canAfford, oi.OrderItemQuantity - oi.PaidQuantity);
                        if (payQty > 0) { oi.PaidQuantity += payQty; budget -= payQty * oi.OrderItemUnitPrice; }
                    }
                }

                decimal newTotalPaid = alreadyPaid + dto.PaymentAmount;
                bool isClosed = newTotalPaid >= netTotal - 0.01m;

                if (isClosed)
                {
                    foreach (var oi in order.OrderItems.Where(x => x.OrderItemStatus != OrderItemStatus.Cancelled)) // [ENUM]
                        oi.PaidQuantity = oi.OrderItemQuantity;

                    order.OrderStatus = OrderStatus.Paid;      // [ENUM]
                    order.OrderClosedAt = DateTime.UtcNow;
                    if (order.Table != null) order.Table.TableStatus = 0;
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                if (isClosed)
                    _ = NotifyDashboardAsync("✅",
                        $"{order.Table?.TableName ?? $"#{order.OrderId}"} hesabını kapattı — ₺{netTotal:N0}",
                        "#22c55e");

                var leftover = netTotal - newTotalPaid;
                return new(true,
                    isClosed ? "Adisyon kapatıldı, ödeme tamamlandı."
                             : $"₺{dto.PaymentAmount:N2} alındı. Kalan: ₺{leftover:N2}",
                    new PaymentResult(isClosed, Math.Max(0, leftover), netTotal));
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return new(false, "Ödeme kaydedilirken hata oluştu: " + ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  6. CloseAsync — Tek Seferlik Tam Ödeme
        // ═══════════════════════════════════════════════════════════════════════
        public async Task<ServiceResult> CloseAsync(OrderCloseDto dto)
        {
            if (dto.PaymentAmount <= 0)
                return new(false, "Geçerli bir tutar giriniz.");

            var order = await _db.Orders
                .Include(o => o.Table)
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.OrderId == dto.OrderId);

            if (order == null)
                return new(false, "Adisyon bulunamadı.");
            if (order.OrderStatus != OrderStatus.Open)  // [ENUM]
                return new(false, "Bu adisyon zaten kapatılmış.");
            if (dto.PaymentAmount < order.OrderTotalAmount)
                return new(false, "Ödeme tutarı toplam tutardan az olamaz.");

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                _db.Payments.Add(new Payment
                {
                    OrderId = dto.OrderId,
                    PaymentsMethod = dto.PaymentMethod == "card" ? 1 : 0,
                    PaymentsAmount = dto.PaymentAmount,
                    PaymentsChangeGiven = dto.PaymentAmount - order.OrderTotalAmount,
                    PaymentsPaidAt = DateTime.UtcNow,
                    PaymentsNote = ""
                });

                foreach (var oi in order.OrderItems.Where(x => x.OrderItemStatus != OrderItemStatus.Cancelled)) // [ENUM]
                    oi.PaidQuantity = oi.OrderItemQuantity;

                order.OrderStatus = OrderStatus.Paid;  // [ENUM]
                order.OrderClosedAt = DateTime.UtcNow;
                var tableName = order.Table?.TableName ?? $"#{order.OrderId}";
                var total = order.OrderTotalAmount;
                if (order.Table != null) order.Table.TableStatus = 0;

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                _ = NotifyDashboardAsync("✅",
                    $"{tableName} hesabını kapattı — ₺{total:N0}", "#22c55e");

                return new(true, "Adisyon kapatıldı, ödeme alındı.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return new(false, "Adisyon kapatılırken hata oluştu: " + ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  7. CloseZeroAsync — Sıfır Tutarlı Adisyon İptali
        // ═══════════════════════════════════════════════════════════════════════
        public async Task<ServiceResult> CloseZeroAsync(OrderCloseZeroDto dto)
        {
            var order = await _db.Orders
                .Include(o => o.Table)
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.OrderId == dto.OrderId);

            if (order == null)
                return new(false, "Adisyon bulunamadı.");
            if (order.OrderStatus != OrderStatus.Open)  // [ENUM]
                return new(false, "Bu adisyon zaten kapatılmış.");
            if (order.OrderTotalAmount > 0.001m)
                return new(false, "Adisyon tutarı sıfır olmadığı için bu yöntemle kapatılamaz.");

            bool hasActiveItems = order.OrderItems.Any(oi =>
                oi.OrderItemStatus != OrderItemStatus.Cancelled && // [ENUM]
                (oi.OrderItemQuantity - oi.CancelledQuantity) > 0);

            if (hasActiveItems)
                return new(false, "Adisyonda hâlâ aktif ürünler var. Önce tüm ürünleri iptal edin.");

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var tableName = order.Table?.TableName ?? $"#{order.OrderId}";
                order.OrderStatus = OrderStatus.Cancelled; // [ENUM]
                order.OrderClosedAt = DateTime.UtcNow;
                if (order.Table != null) order.Table.TableStatus = 0;

                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                _ = NotifyDashboardAsync("🚫",
                    $"{tableName} — sıfır tutarlı adisyon iptal edildi", "#ef4444");

                return new(true, "Sıfır tutarlı adisyon kapatıldı, masa boşaltıldı.");
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return new(false, "Adisyon kapatılırken hata oluştu: " + ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  8. CancelItemAsync — Kalem İptali
        // ═══════════════════════════════════════════════════════════════════════
        public async Task<ServiceResult<CancelItemResult>> CancelItemAsync(OrderItemCancelDto dto)
        {
            if (dto.CancelQty < 1)
                return new(false, "İptal miktarı en az 1 olmalıdır.");

            var item = await _db.OrderItems
                .Include(oi => oi.MenuItem)
                .FirstOrDefaultAsync(oi => oi.OrderItemId == dto.OrderItemId);
            var order = await _db.Orders.FindAsync(dto.OrderId);

            if (item == null || order == null)
                return new(false, "Kalem veya adisyon bulunamadı.");
            if (order.OrderStatus != OrderStatus.Open)  // [ENUM]
                return new(false, "Kapalı adisyonda iptal yapılamaz.");

            int activeQty = item.OrderItemQuantity - item.CancelledQuantity;
            int cancelable = activeQty - item.PaidQuantity;

            if (dto.CancelQty > cancelable)
                return new(false,
                    $"En fazla {cancelable} adet iptal edilebilir ({item.PaidQuantity} adet zaten ödendi).");

            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                decimal refund = item.OrderItemUnitPrice * dto.CancelQty;

                item.CancelledQuantity += dto.CancelQty;
                item.CancelReason = string.IsNullOrWhiteSpace(dto.CancelReason) ? null : dto.CancelReason.Trim();
                item.OrderItemLineTotal = item.OrderItemUnitPrice * (item.OrderItemQuantity - item.CancelledQuantity);

                if (item.OrderItemQuantity - item.CancelledQuantity <= 0)
                    item.OrderItemStatus = OrderItemStatus.Cancelled; // [ENUM]

                order.OrderTotalAmount = Math.Max(0, order.OrderTotalAmount - refund);

                bool tracksStock = item.MenuItem?.TrackStock == true;
                bool isWasted = false;

                if (tracksStock)
                {
                    isWasted = dto.IsWasted ?? false;
                    item.IsWasted = isWasted;
                    int prevStock = item.MenuItem!.StockQuantity;

                    if (!isWasted)
                    {
                        item.MenuItem.StockQuantity += dto.CancelQty;
                        if (!item.MenuItem.IsAvailable && item.MenuItem.StockQuantity > 0)
                            item.MenuItem.IsAvailable = true;
                    }

                    _db.StockLogs.Add(new StockLog
                    {
                        MenuItemId = item.MenuItem!.MenuItemId,
                        MovementType = isWasted ? "Çıkış" : "Giriş",
                        QuantityChange = isWasted ? -dto.CancelQty : dto.CancelQty,
                        PreviousStock = prevStock,
                        NewStock = isWasted ? prevStock : prevStock + dto.CancelQty,
                        Note = isWasted
                            ? $"Zayi/Fire — Adisyon #{dto.OrderId}, {dto.CancelQty} adet"
                            : $"İptal iadesi — Adisyon #{dto.OrderId}, {dto.CancelQty} adet",
                        SourceType = isWasted ? "SiparişKaynaklı" : null,
                        OrderId = isWasted ? dto.OrderId : null,
                        UnitPrice = item.OrderItemUnitPrice,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    item.IsWasted = null;
                }

                await _db.SaveChangesAsync();

                // Kalan tutar sıfırlandıysa otomatik kapat
                var freshPaid = await _db.Payments
                    .Where(p => p.OrderId == order.OrderId)
                    .SumAsync(p => p.PaymentsAmount);

                bool orderAutoClose = false;
                if (order.OrderStatus == OrderStatus.Open && // [ENUM]
                    order.OrderTotalAmount - freshPaid <= 0.001m &&
                    freshPaid > 0)
                {
                    var tableForClose = await _db.Tables.FindAsync(order.TableId);
                    order.OrderStatus = OrderStatus.Paid; // [ENUM]
                    order.OrderClosedAt = DateTime.UtcNow;
                    if (tableForClose != null) tableForClose.TableStatus = 0;
                    await _db.SaveChangesAsync();
                    orderAutoClose = true;
                }

                await tx.CommitAsync();

                return new(true,
                    $"{dto.CancelQty} adet iptal edildi." +
                    (tracksStock ? (isWasted ? " | Zayi kaydedildi." : " | Stoka iade edildi.") : ""),
                    new CancelItemResult(orderAutoClose, tracksStock, isWasted));
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return new(false, "İptal işleminde hata oluştu: " + ex.Message);
            }
        }
    }
}
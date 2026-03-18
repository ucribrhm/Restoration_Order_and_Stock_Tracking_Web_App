// ============================================================================
//  Services/DashboardService.cs
//
//  REFACTORING: HomeController.Index() + HomeController.GetLiveMetrics()
//               → DashboardService
//
//  ESKİ DURUM:
//    HomeController → 540+ satır → 2 action → 16+ DB sorgusu →
//    her HTTP isteğinde tüm sorgular çalışır →
//    100 kullanıcıda DB'ye saatte ~57.600 hit.
//
//  YENİ DURUM:
//    DashboardService → IMemoryCache → 30sn cache →
//    100 kullanıcıda DB'ye saatte ~240 hit (240x azalma).
//
//  CACHE STRATEJİSİ:
//    dashboard_data_{tenantId}  → GetDashboardDataAsync  (Index için)
//    dashboard_live_{tenantId}  → GetLiveMetricsAsync    (AJAX için)
//    Süre: AbsoluteExpirationRelativeToNow = 30 saniye
//    Tenant izolasyonu: her tenant kendi key'inde bağımsız önbelleklenir.
//
//  DB SORGU LİSTESİ (her iki metot için ortak private metodlarda):
//    1. todayOrders       → bugünkü KPI (açık + ödenmiş)
//    2. yesterdayRevenue  → trend yüzdesi       (yalnızca Index)
//    3. allTables         → masa durumu + ısı haritası
//    4. openOrders        → ısı haritası adisyon detayı
//    5. rawTopProducts    → en çok satan 5 ürün (bugün)
//    6. thisWeekOrders    → haftalık trend - bu hafta
//    7. lastWeekOrders    → haftalık trend - geçen hafta
//    8. stockAlertItems   → stok uyarı kartları
//    9. lowStockCount     → stok uyarı badge sayısı  (yalnızca Index)
//
//  PERFORMANS NOTLARI:
//    - Tüm sorgularda AsNoTracking() kullanılır (dashboard salt okunur).
//    - todayOrderIds boşsa rawTopProducts sorgusu çalıştırılmaz.
//    - Saatlik trend ve haftalık vektör bellekte hesaplanır (DB'ye gidilmez).
// ============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared.Common;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Dashboard;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly RestaurantDbContext _db;
        private readonly IMemoryCache _cache;

        // ── Masa durumu sabitleri ─────────────────────────────────────────────
        private const int TABLE_EMPTY = 0;
        private const int TABLE_OCCUPIED = 1;
        private const int TABLE_RESERVED = 2;

        // ── Haftalık trend için Türkçe gün adları ────────────────────────────
        private static readonly string[] TurkishDayNames =
            { "Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt" };

        // ── Cache süre sabiti ─────────────────────────────────────────────────
        private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

        public DashboardService(RestaurantDbContext db, IMemoryCache cache)
        {
            _db = db;
            _cache = cache;
        }

        // =====================================================================
        //  GetDashboardDataAsync  —  Index() action için DashboardViewModel
        // =====================================================================
        public async Task<DashboardViewModel> GetDashboardDataAsync(string tenantId)
        {
            var cacheKey = $"dashboard_data_{tenantId}";

            if (_cache.TryGetValue(cacheKey, out DashboardViewModel? cached) && cached is not null)
                return cached;

            var vm = await BuildDashboardViewModelAsync();

            _cache.Set(cacheKey, vm, new MemoryCacheEntryOptions
            {
                Size = 1,
                AbsoluteExpirationRelativeToNow = CacheDuration,
                Priority = CacheItemPriority.Normal
            });

            return vm;
        }

        // =====================================================================
        //  GetLiveMetricsAsync  —  GetLiveMetrics() action için JSON objesi
        //  SignalR eventi geldiğinde JS bu endpoint'i fetch ile çağırır.
        // =====================================================================
        public async Task<object> GetLiveMetricsAsync(string tenantId)
        {
            var cacheKey = $"dashboard_live_{tenantId}";

            if (_cache.TryGetValue(cacheKey, out object? cached) && cached is not null)
                return cached;

            var result = await BuildLiveMetricsAsync();

            _cache.Set(cacheKey, result, new MemoryCacheEntryOptions
            {
                Size = 1,
                AbsoluteExpirationRelativeToNow = CacheDuration,
                Priority = CacheItemPriority.Normal
            });

            return result;
        }

        // =====================================================================
        //  PRIVATE — BuildDashboardViewModelAsync
        //  Index() için tüm DB sorguları ve ViewModel mapping burada.
        // =====================================================================
        private async Task<DashboardViewModel> BuildDashboardViewModelAsync()
        {
            // ── UTC zaman aralıkları ─────────────────────────────────────────
            var todayLocal = DateTime.Today;
            var todayUtcStart = todayLocal.ToUniversalTime();
            var todayUtcEnd = todayLocal.AddDays(1).ToUniversalTime();
            var yesterdayUtcStart = todayLocal.AddDays(-1).ToUniversalTime();
            var yesterdayUtcEnd = todayUtcStart;

            // =================================================================
            //  1. TEPE METRİKLER
            //     Tek sorguda bugünkü siparişlerin tüm KPI verisini çek.
            // =================================================================
            var todayOrders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.OrderOpenedAt >= todayUtcStart &&
                            o.OrderOpenedAt < todayUtcEnd)
                .Select(o => new
                {
                    o.OrderId,
                    o.OrderStatus,
                    o.OrderTotalAmount,
                    o.OrderOpenedAt
                })
                .ToListAsync();

            var paidToday = todayOrders.Where(o => o.OrderStatus == OrderStatus.Paid).ToList();
            decimal dailyRevenue = paidToday.Sum(o => o.OrderTotalAmount);
            int totalOrders = todayOrders.Count;
            int activeOrders = todayOrders.Count(o => o.OrderStatus == OrderStatus.Open);
            int closedOrders = paidToday.Count;
            decimal avgOrderValue = closedOrders > 0
                ? Math.Round(dailyRevenue / closedOrders, 2) : 0m;

            // ── Dünkü ciro (trend yüzdesi için) ─────────────────────────────
            decimal yesterdayRevenue = await _db.Orders
                .AsNoTracking()
                .Where(o => o.OrderStatus == OrderStatus.Paid &&
                            o.OrderOpenedAt >= yesterdayUtcStart &&
                            o.OrderOpenedAt < yesterdayUtcEnd)
                .SumAsync(o => (decimal?)o.OrderTotalAmount) ?? 0m;

            decimal? trendPct = yesterdayRevenue > 0
                ? Math.Round((dailyRevenue - yesterdayRevenue) / yesterdayRevenue * 100, 1)
                : dailyRevenue > 0 ? 100m : null;

            // =================================================================
            //  2. MASA DURUMU  (TableStatus: 0=Boş, 1=Dolu, 2=Rezerve)
            //     Tek sorguda hem özet hem ısı haritası verisi.
            // =================================================================
            var allTables = await _db.Tables
                .AsNoTracking()
                .OrderBy(t => t.TableId)
                .Select(t => new
                {
                    t.TableId,
                    t.TableName,
                    t.TableStatus,
                    t.TableCapacity,
                    t.IsWaiterCalled,
                    t.ReservationGuestCount,
                    t.ReservationTime
                })
                .ToListAsync();

            var tableStatus = new TableStatusSummaryData
            {
                Empty = allTables.Count(t => t.TableStatus == TABLE_EMPTY),
                Occupied = allTables.Count(t => t.TableStatus == TABLE_OCCUPIED),
                Reserved = allTables.Count(t => t.TableStatus == TABLE_RESERVED)
            };

            int waiterCalls = allTables.Count(t => t.IsWaiterCalled);

            // =================================================================
            //  3. ISI HARİTASI
            //     Açık adisyonları tek sorguda çek, bellekte masa ile eşleştir.
            // =================================================================
            var openOrders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.OrderStatus == OrderStatus.Open)
                .Select(o => new
                {
                    o.TableId,
                    o.OrderTotalAmount,
                    o.OrderOpenedAt
                })
                .ToListAsync();

            var openOrderByTable = openOrders
                .GroupBy(o => o.TableId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(o => o.OrderOpenedAt).First()
                );

            var now = DateTime.UtcNow;

            var heatmap = allTables.Select(t =>
            {
                string hmStatus = t.TableStatus switch
                {
                    TABLE_OCCUPIED => t.IsWaiterCalled ? "waiter" : "occupied",
                    TABLE_RESERVED => "reserved",
                    _ => "empty"
                };

                decimal bill = 0m;
                string durationTxt = string.Empty;
                int guestCount = 0;

                if (hmStatus is "occupied" or "waiter" &&
                    openOrderByTable.TryGetValue(t.TableId, out var activeOrder))
                {
                    bill = activeOrder.OrderTotalAmount;
                    var elapsed = now - activeOrder.OrderOpenedAt;
                    durationTxt = elapsed.TotalMinutes < 60
                        ? $"{(int)elapsed.TotalMinutes}dk"
                        : $"{(int)elapsed.TotalHours}s {elapsed.Minutes}dk";
                    guestCount = t.TableCapacity;
                }
                else if (hmStatus == "reserved")
                {
                    guestCount = t.ReservationGuestCount ?? t.TableCapacity;
                }

                return new TableHeatmapItem
                {
                    TableId = t.TableId,
                    TableName = t.TableName,
                    Status = hmStatus,
                    CurrentBill = bill,
                    GuestCount = guestCount,
                    DurationText = durationTxt,
                    ReservationTime = t.ReservationTime.HasValue
                        ? t.ReservationTime.Value.ToLocalTime().ToString("HH:mm")
                        : null
                };
            }).ToList();

            // =================================================================
            //  4. SAATLİK YOĞUNLUK  (AreaChart — 08:00 – 23:00)
            //     todayOrders bellekte → DB'ye gidilmez.
            // =================================================================
            var hourlyTrends = Enumerable.Range(8, 16)
                .Select(h =>
                {
                    var slotStart = todayUtcStart.AddHours(h);
                    var slotEnd = slotStart.AddHours(1);
                    var inSlot = todayOrders
                        .Where(o => o.OrderOpenedAt >= slotStart &&
                                    o.OrderOpenedAt < slotEnd)
                        .ToList();
                    return new HourlyTrendPoint
                    {
                        HourLabel = $"{h:D2}:00",
                        OrderCount = inSlot.Count,
                        Revenue = inSlot
                            .Where(o => o.OrderStatus == OrderStatus.Paid)
                            .Sum(o => o.OrderTotalAmount)
                    };
                })
                .ToList();

            // =================================================================
            //  5. EN ÇOK SATANLAR  (Doughnut Chart — Top 5)
            //     todayOrders boşsa DB turu yapma.
            // =================================================================
            var todayOrderIds = todayOrders.Select(o => o.OrderId).ToList();
            List<TopProductPoint> topProducts = new();

            if (todayOrderIds.Count > 0)
            {
                var rawTopProducts = await _db.OrderItems
                    .AsNoTracking()
                    .Where(oi => todayOrderIds.Contains(oi.OrderId) &&
                                 oi.CancelledQuantity < oi.OrderItemQuantity)
                    .Select(oi => new
                    {
                        oi.MenuItemId,
                        ProductName = oi.MenuItem.MenuItemName,
                        ActiveQty = oi.OrderItemQuantity - oi.CancelledQuantity,
                        LineTotal = oi.OrderItemLineTotal
                    })
                    .ToListAsync();

                topProducts = rawTopProducts
                    .GroupBy(x => new { x.MenuItemId, x.ProductName })
                    .Select(g => new TopProductPoint
                    {
                        ProductName = g.Key.ProductName,
                        Quantity = g.Sum(x => x.ActiveQty),
                        Revenue = g.Sum(x => x.LineTotal)
                    })
                    .OrderByDescending(x => x.Quantity)
                    .Take(5)
                    .ToList();
            }

            // =================================================================
            //  6. HAFTALIK TREND KARŞILAŞTIRMASI  (LineChart)
            //     Bu hafta: son 7 gün (bugün dahil)
            //     Geçen hafta: 8–14 gün önce
            // =================================================================
            var thisWeekStart = todayLocal.AddDays(-6).ToUniversalTime();
            var lastWeekStart = todayLocal.AddDays(-13).ToUniversalTime();
            var lastWeekEnd = thisWeekStart;

            var thisWeekOrders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.OrderStatus == OrderStatus.Paid &&
                            o.OrderOpenedAt >= thisWeekStart &&
                            o.OrderOpenedAt < todayUtcEnd)
                .Select(o => new { o.OrderOpenedAt, o.OrderTotalAmount })
                .ToListAsync();

            var lastWeekOrders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.OrderStatus == OrderStatus.Paid &&
                            o.OrderOpenedAt >= lastWeekStart &&
                            o.OrderOpenedAt < lastWeekEnd)
                .Select(o => new { o.OrderOpenedAt, o.OrderTotalAmount })
                .ToListAsync();

            var weeklyTrend = Enumerable.Range(0, 7)
                .Select(i =>
                {
                    var dayLocal = todayLocal.AddDays(i - 6);
                    var dayUtcStart = dayLocal.ToUniversalTime();
                    var dayUtcEnd = dayLocal.AddDays(1).ToUniversalTime();

                    decimal tw = thisWeekOrders
                        .Where(o => o.OrderOpenedAt >= dayUtcStart &&
                                    o.OrderOpenedAt < dayUtcEnd)
                        .Sum(o => o.OrderTotalAmount);

                    decimal lw = lastWeekOrders
                        .Where(o => o.OrderOpenedAt >= dayUtcStart.AddDays(-7) &&
                                    o.OrderOpenedAt < dayUtcEnd.AddDays(-7))
                        .Sum(o => o.OrderTotalAmount);

                    return new WeeklyTrendPoint
                    {
                        DayLabel = TurkishDayNames[(int)dayLocal.DayOfWeek],
                        ThisWeek = tw,
                        LastWeek = lw
                    };
                })
                .ToList();

            // =================================================================
            //  7. STOK UYARILARI
            //     TrackStock=true, AlertThreshold aşılmış, silinmemiş ürünler.
            // =================================================================
            var stockAlertItems = await _db.MenuItems
                .AsNoTracking()
                .Where(m => !m.IsDeleted &&
                             m.TrackStock &&
                             m.AlertThreshold > 0 &&
                             m.StockQuantity <= m.AlertThreshold)
                .OrderBy(m => m.StockQuantity)
                .Take(5)
                .Select(m => new
                {
                    m.MenuItemName,
                    m.StockQuantity,
                    m.AlertThreshold,
                    m.CriticalThreshold
                })
                .ToListAsync();

            var stockAlerts = stockAlertItems.Select(m =>
            {
                string level = m.StockQuantity <= m.CriticalThreshold && m.CriticalThreshold > 0
                    ? "critical" : "low";

                return new StockAlertItem
                {
                    ProductName = m.MenuItemName,
                    CurrentQty = m.StockQuantity,
                    AlertThreshold = m.AlertThreshold,
                    Unit = "ad",
                    Level = level
                };
            }).ToList();

            // =================================================================
            //  8. DÜŞÜK STOK BADGE SAYISI
            //     Take(5) uygulanmaz — tüm uyarılar sayılır.
            // =================================================================
            int lowStockCount = await _db.MenuItems
                .AsNoTracking()
                .CountAsync(m => !m.IsDeleted &&
                                  m.TrackStock &&
                                  m.AlertThreshold > 0 &&
                                  m.StockQuantity <= m.AlertThreshold);

            // =================================================================
            //  ViewModel'i Derle ve Döndür
            // =================================================================
            return new DashboardViewModel
            {
                DailyTotalRevenue = dailyRevenue,
                RevenueTrendPercentage = trendPct,
                YesterdayRevenue = yesterdayRevenue,
                TotalOrdersToday = totalOrders,
                ActiveOrdersNow = activeOrders,
                ClosedOrdersToday = closedOrders,
                AverageOrderValue = avgOrderValue,
                DailyRevenueTarget = 10_000m,
                TableStatus = tableStatus,
                TableHeatmap = heatmap,
                HourlyOrderTrends = hourlyTrends,
                TopSellingProducts = topProducts,
                WeeklyTrend = weeklyTrend,
                StockAlerts = stockAlerts,
                LowStockAlerts = lowStockCount,
                WaiterCallsActive = waiterCalls,
            };
        }

        // =====================================================================
        //  PRIVATE — BuildLiveMetricsAsync
        //  GetLiveMetrics() AJAX endpoint için JSON objesi üretir.
        //  Index sayfasından farklı olarak DashboardViewModel değil,
        //  JS'in doğrudan tükettiği düz anonim obje döner.
        // =====================================================================
        private async Task<object> BuildLiveMetricsAsync()
        {
            var todayLocal = DateTime.Today;
            var todayUtcStart = todayLocal.ToUniversalTime();
            var todayUtcEnd = todayLocal.AddDays(1).ToUniversalTime();

            // =================================================================
            //  1. KPI: Bugünkü siparişler
            // =================================================================
            var todayOrders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.OrderOpenedAt >= todayUtcStart &&
                            o.OrderOpenedAt < todayUtcEnd)
                .Select(o => new
                {
                    o.OrderId,
                    o.OrderStatus,
                    o.OrderTotalAmount,
                    o.OrderOpenedAt
                })
                .ToListAsync();

            var paidOrders = todayOrders.Where(o => o.OrderStatus == OrderStatus.Paid).ToList();
            decimal revenue = paidOrders.Sum(o => o.OrderTotalAmount);
            int totalOrders = todayOrders.Count;
            int activeOrders = todayOrders.Count(o => o.OrderStatus == OrderStatus.Open);
            int closedOrders = paidOrders.Count;
            decimal avgOrder = closedOrders > 0 ? Math.Round(revenue / closedOrders, 2) : 0m;

            // =================================================================
            //  2. Masa durumu + Heatmap
            // =================================================================
            var allTables = await _db.Tables
                .AsNoTracking()
                .OrderBy(t => t.TableId)
                .Select(t => new
                {
                    t.TableId,
                    t.TableName,
                    t.TableStatus,
                    t.TableCapacity,
                    t.IsWaiterCalled,
                    t.ReservationGuestCount,
                    t.ReservationTime
                })
                .ToListAsync();

            int occupied = allTables.Count(t => t.TableStatus == TABLE_OCCUPIED);
            int reserved = allTables.Count(t => t.TableStatus == TABLE_RESERVED);
            int empty = allTables.Count(t => t.TableStatus == TABLE_EMPTY);
            int total = allTables.Count;
            int waiterCalls = allTables.Count(t => t.IsWaiterCalled);

            var openOrders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.OrderStatus == OrderStatus.Open)
                .Select(o => new { o.TableId, o.OrderTotalAmount, o.OrderOpenedAt })
                .ToListAsync();

            var openOrderByTable = openOrders
                .GroupBy(o => o.TableId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(o => o.OrderOpenedAt).First()
                );

            var now = DateTime.UtcNow;

            var heatmap = allTables.Select(t =>
            {
                string hmStatus = t.TableStatus switch
                {
                    TABLE_OCCUPIED => t.IsWaiterCalled ? "waiter" : "occupied",
                    TABLE_RESERVED => "reserved",
                    _ => "empty"
                };

                decimal bill = 0m;
                string durationTxt = string.Empty;
                int guestCount = 0;
                string? reservationTime = null;

                if (hmStatus is "occupied" or "waiter" &&
                    openOrderByTable.TryGetValue(t.TableId, out var ao))
                {
                    bill = ao.OrderTotalAmount;
                    var elapsed = now - ao.OrderOpenedAt;
                    durationTxt = elapsed.TotalMinutes < 60
                        ? $"{(int)elapsed.TotalMinutes}dk"
                        : $"{(int)elapsed.TotalHours}s {elapsed.Minutes}dk";
                    guestCount = t.TableCapacity;
                }
                else if (hmStatus == "reserved")
                {
                    guestCount = t.ReservationGuestCount ?? t.TableCapacity;
                    reservationTime = t.ReservationTime.HasValue
                        ? t.ReservationTime.Value.ToLocalTime().ToString("HH:mm")
                        : null;
                }

                return new
                {
                    tableId = t.TableId,
                    tableName = t.TableName,
                    status = hmStatus,
                    currentBill = bill,
                    guestCount,
                    durationText = durationTxt,
                    reservationTime
                };
            }).ToList<object>();

            // =================================================================
            //  3. Saatlik trend (AreaChart)
            // =================================================================
            var hourlyTrends = Enumerable.Range(8, 16)
                .Select(h =>
                {
                    var slotStart = todayUtcStart.AddHours(h);
                    var slotEnd = slotStart.AddHours(1);
                    var inSlot = todayOrders
                        .Where(o => o.OrderOpenedAt >= slotStart &&
                                    o.OrderOpenedAt < slotEnd)
                        .ToList();
                    return (object)new
                    {
                        lbl = $"{h:D2}:00",
                        n = inSlot.Count,
                        c = (double)inSlot
                                .Where(o => o.OrderStatus == OrderStatus.Paid)
                                .Sum(o => o.OrderTotalAmount)
                    };
                })
                .ToList();

            // =================================================================
            //  4. En çok satanlar (Doughnut Chart — Top 5)
            // =================================================================
            var todayOrderIds = todayOrders.Select(o => o.OrderId).ToList();
            List<object> topProducts = new();

            if (todayOrderIds.Count > 0)
            {
                var rawTop = await _db.OrderItems
                    .AsNoTracking()
                    .Where(oi => todayOrderIds.Contains(oi.OrderId) &&
                                 oi.CancelledQuantity < oi.OrderItemQuantity)
                    .Select(oi => new
                    {
                        oi.MenuItemId,
                        ProductName = oi.MenuItem.MenuItemName,
                        ActiveQty = oi.OrderItemQuantity - oi.CancelledQuantity,
                        LineTotal = oi.OrderItemLineTotal
                    })
                    .ToListAsync();

                topProducts = rawTop
                    .GroupBy(x => new { x.MenuItemId, x.ProductName })
                    .Select(g => (object)new
                    {
                        nm = g.Key.ProductName,
                        q = g.Sum(x => x.ActiveQty),
                        r = (double)g.Sum(x => x.LineTotal)
                    })
                    .OrderByDescending(x => ((dynamic)x).q)
                    .Take(5)
                    .ToList();
            }

            // =================================================================
            //  5. Haftalık trend (LineChart)
            // =================================================================
            var thisWeekStart = todayLocal.AddDays(-6).ToUniversalTime();
            var lastWeekStart = todayLocal.AddDays(-13).ToUniversalTime();
            var lastWeekEnd = thisWeekStart;

            var thisWeekOrders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.OrderStatus == OrderStatus.Paid &&
                            o.OrderOpenedAt >= thisWeekStart &&
                            o.OrderOpenedAt < todayUtcEnd)
                .Select(o => new { o.OrderOpenedAt, o.OrderTotalAmount })
                .ToListAsync();

            var lastWeekOrders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.OrderStatus == OrderStatus.Paid &&
                            o.OrderOpenedAt >= lastWeekStart &&
                            o.OrderOpenedAt < lastWeekEnd)
                .Select(o => new { o.OrderOpenedAt, o.OrderTotalAmount })
                .ToListAsync();

            var weeklyTrend = Enumerable.Range(0, 7)
                .Select(i =>
                {
                    var dayLocal = todayLocal.AddDays(i - 6);
                    var dayUtcStart = dayLocal.ToUniversalTime();
                    var dayUtcEnd = dayLocal.AddDays(1).ToUniversalTime();

                    decimal tw = thisWeekOrders
                        .Where(o => o.OrderOpenedAt >= dayUtcStart &&
                                    o.OrderOpenedAt < dayUtcEnd)
                        .Sum(o => o.OrderTotalAmount);

                    decimal lw = lastWeekOrders
                        .Where(o => o.OrderOpenedAt >= dayUtcStart.AddDays(-7) &&
                                    o.OrderOpenedAt < dayUtcEnd.AddDays(-7))
                        .Sum(o => o.OrderTotalAmount);

                    return (object)new
                    {
                        d = TurkishDayNames[(int)dayLocal.DayOfWeek],
                        tw = (double)tw,
                        lw = (double)lw
                    };
                })
                .ToList();

            // =================================================================
            //  6. Stok uyarıları
            // =================================================================
            var stockItems = await _db.MenuItems
                .AsNoTracking()
                .Where(m => !m.IsDeleted &&
                             m.TrackStock &&
                             m.AlertThreshold > 0 &&
                             m.StockQuantity <= m.AlertThreshold)
                .OrderBy(m => m.StockQuantity)
                .Take(5)
                .Select(m => new
                {
                    m.MenuItemName,
                    m.StockQuantity,
                    m.AlertThreshold,
                    m.CriticalThreshold
                })
                .ToListAsync();

            var stockAlerts = stockItems.Select(m =>
            {
                string level = m.StockQuantity <= m.CriticalThreshold && m.CriticalThreshold > 0
                    ? "critical" : "low";
                decimal maxQty = m.AlertThreshold > 0
                    ? m.AlertThreshold * 3
                    : Math.Max(m.StockQuantity * 2, 1);
                decimal pct = maxQty > 0
                    ? Math.Round(m.StockQuantity / maxQty * 100, 1) : 0;

                return new
                {
                    productName = m.MenuItemName,
                    currentQty = m.StockQuantity,
                    maxQty,
                    alertThreshold = m.AlertThreshold,
                    unit = "ad",
                    level,
                    percentRemaining = pct
                };
            }).ToList<object>();

            // =================================================================
            //  7. Ciro hedefi
            // =================================================================
            const decimal dailyTarget = 10_000m;
            decimal targetPct = dailyTarget > 0
                ? Math.Min(Math.Round(revenue / dailyTarget * 100, 1), 100m) : 0m;

            // =================================================================
            //  JSON yanıtı oluştur ve döndür
            // =================================================================
            return new
            {
                // KPI
                dailyRevenue = revenue,
                totalOrders,
                activeOrders,
                closedOrders,
                avgOrder,
                waiterCalls,
                // Masa özet
                occupied,
                reserved,
                empty,
                total,
                occupancyRate = total > 0
                    ? Math.Round((double)occupied / total * 100, 1) : 0d,
                // Kompleks
                tableHeatmap = heatmap,
                hourlyTrends,
                topProducts,
                weeklyTrend,
                stockAlerts,
                // Hedef
                dailyRevenueTarget = (double)dailyTarget,
                revenueTargetPct = (double)targetPct
            };
        }
    }
}
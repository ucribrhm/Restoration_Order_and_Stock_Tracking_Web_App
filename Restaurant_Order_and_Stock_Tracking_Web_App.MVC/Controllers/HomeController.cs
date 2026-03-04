// ════════════════════════════════════════════════════════════════════════════
//  Controllers/HomeController.cs  —  PRODUCTION (Gerçek Veri)
//  Yol: Restaurant_Order_and_Stock_Tracking_Web_App.MVC/Controllers/
//
//  Bu dosya tamamen gerçek DB sorgularından oluşmaktadır.
//  Mock veri içermez. DbContext: RestaurantDbContext
//  Veritabanı: PostgreSQL (UTC timestamp kullanımı zorunlu)
//
//  ⚡ Index.cshtml ve DashboardViewModel ile TAMAMEN UYUMLUDUR.
//     Razor view'da hiçbir değişiklik gerekmez.
// ════════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared.Common;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Dashboard;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Controllers
{
    [Authorize(Roles = "Admin")] // Bu satır çok kritik!
    public class HomeController : Controller
    {
        private readonly RestaurantDbContext _db;

        // ── Sipariş durum sabitleri (ReportsController ile tutarlı) ─────────
        // [ENUM] STATUS_OPEN kaldırıldı — OrderStatus.Open kullanın
        // [ENUM] STATUS_PAID kaldırıldı — OrderStatus.Paid kullanın

        // ── Table.TableStatus integer sabitleri ──────────────────────────────
        private const int TABLE_EMPTY = 0;
        private const int TABLE_OCCUPIED = 1;
        private const int TABLE_RESERVED = 2;

        // ── Türkçe gün adları (WeeklyTrend DayLabel için) ───────────────────
        private static readonly string[] TurkishDayNames =
            { "Paz", "Pzt", "Sal", "Çar", "Per", "Cum", "Cmt" };

        public HomeController(RestaurantDbContext db) => _db = db;

        // ════════════════════════════════════════════════════════════════════
        //  GET  /Home/Index  —  Dashboard ana sayfa
        // ════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Dashboard";

            // ── UTC zaman aralıkları ─────────────────────────────────────────
            // PostgreSQL UTC kullandığı için DateTime.Today.ToUniversalTime()
            // kullanıyoruz. SQL Server projelerinde DateTime.Today kullanılabilir.
            var todayLocal = DateTime.Today;
            var todayUtcStart = todayLocal.ToUniversalTime();
            var todayUtcEnd = todayLocal.AddDays(1).ToUniversalTime();

            var yesterdayUtcStart = todayLocal.AddDays(-1).ToUniversalTime();
            var yesterdayUtcEnd = todayUtcStart;

            // ════════════════════════════════════════════════════════════════
            //  1. TEPE METRİKLER
            //     Tek sorguda bugünkü siparişlerin tüm KPI verisini çek.
            // ════════════════════════════════════════════════════════════════

            // Bugün açılan tüm siparişler (open + paid) — hafif projeksiyon
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

            var paidToday = todayOrders
                .Where(o => o.OrderStatus == OrderStatus.Paid)
                .ToList();

            decimal dailyRevenue = paidToday.Sum(o => o.OrderTotalAmount);
            int totalOrders = todayOrders.Count;
            int activeOrders = todayOrders.Count(o => o.OrderStatus == OrderStatus.Open);
            int closedOrders = paidToday.Count;

            decimal avgOrderValue = closedOrders > 0
                ? Math.Round(dailyRevenue / closedOrders, 2)
                : 0m;

            // ── Dünkü ciro (trend için) ─────────────────────────────────────
            decimal yesterdayRevenue = await _db.Orders
                .AsNoTracking()
                .Where(o => o.OrderStatus == OrderStatus.Paid &&
                            o.OrderOpenedAt >= yesterdayUtcStart &&
                            o.OrderOpenedAt < yesterdayUtcEnd)
                .SumAsync(o => (decimal?)o.OrderTotalAmount) ?? 0m;

            decimal? trendPct = yesterdayRevenue > 0
                ? Math.Round((dailyRevenue - yesterdayRevenue) / yesterdayRevenue * 100, 1)
                : dailyRevenue > 0 ? 100m : null;

            // ════════════════════════════════════════════════════════════════
            //  2. MASA DURUMU
            //     TableStatus: 0=Boş, 1=Dolu, 2=Rezerve
            // ════════════════════════════════════════════════════════════════

            // Tüm masaları tek sorguda al — hem status özeti hem ısı haritası için
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

            // ════════════════════════════════════════════════════════════════
            //  3. ISI HARİTASI
            //     Dolu/Garson masalar için aktif adisyon bilgisi gerekli.
            //     Tek JOIN sorgusuyla tüm açık adisyonları çek.
            // ════════════════════════════════════════════════════════════════

            // Tüm açık adisyonlar (OrderTotalAmount anlık tutar, OpenedAt süre için)
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

            // TableId → açık adisyon dictionary (bir masada en fazla 1 açık adisyon)
            var openOrderByTable = openOrders
                .GroupBy(o => o.TableId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(o => o.OrderOpenedAt).First()
                );

            var now = DateTime.UtcNow;

            var heatmap = allTables.Select(t =>
            {
                // Isı haritası durum belirleme
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

                    // Adisyon açılış süresi hesabı
                    var elapsed = now - activeOrder.OrderOpenedAt;
                    durationTxt = elapsed.TotalMinutes < 60
                        ? $"{(int)elapsed.TotalMinutes}dk"
                        : $"{(int)elapsed.TotalHours}s {elapsed.Minutes}dk";

                    guestCount = t.TableCapacity; // adisyonda guest yok, kapasite kullanılır
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

            // ════════════════════════════════════════════════════════════════
            //  4. SAATLİK YOĞUNLUK  (AreaChart — 08:00 – 23:00)
            //     todayOrders zaten bellekte; GroupBy ile DB'ye gidilmez.
            // ════════════════════════════════════════════════════════════════

            var hourlyTrends = Enumerable.Range(8, 16) // 8..23
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

            // ════════════════════════════════════════════════════════════════
            //  5. EN ÇOK SATANLAR  (Doughnut Chart)
            //     Bugünkü siparişlerin order_items'larını DB tarafında grupluyoruz.
            //     todayOrders zaten bellekte; ID listesi küçük olduğundan
            //     WHERE IN ile gönderilmesi EF tarafından otomatik yapılır.
            // ════════════════════════════════════════════════════════════════

            var todayOrderIds = todayOrders.Select(o => o.OrderId).ToList();

            // todayOrderIds boşsa gereksiz DB turu yapmayalım
            List<TopProductPoint> topProducts = new();

            if (todayOrderIds.Count > 0)
            {
                // DB tarafında gruplama — bellekte tuple anonymous yerine
                // client-side projection kullanıyoruz çünkü GroupBy translation
                // EF Core'da daha güvenli bu şekilde çalışır.
                var rawTopProducts = await _db.OrderItems
                    .AsNoTracking()
                    .Where(oi =>
                        todayOrderIds.Contains(oi.OrderId) &&
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

            // ════════════════════════════════════════════════════════════════
            //  6. HAFTALIK TREND KARŞILAŞTIRMASI  (LineChart)
            //     Bu hafta: son 7 gün (bugün dahil)
            //     Geçen hafta: 8–14 gün önce
            //     Her iki haftanın günlük cirolarını hizalıyoruz.
            // ════════════════════════════════════════════════════════════════

            var thisWeekStart = todayLocal.AddDays(-6).ToUniversalTime();  // 7 gün penceresi
            var lastWeekStart = todayLocal.AddDays(-13).ToUniversalTime(); // 7 gün önce başlar
            var lastWeekEnd = thisWeekStart;

            // Bu haftanın günlük ciroları (paid, OrderOpenedAt bazlı)
            var thisWeekOrders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.OrderStatus == OrderStatus.Paid &&
                            o.OrderOpenedAt >= thisWeekStart &&
                            o.OrderOpenedAt < todayUtcEnd)
                .Select(o => new { o.OrderOpenedAt, o.OrderTotalAmount })
                .ToListAsync();

            // Geçen haftanın günlük ciroları
            var lastWeekOrders = await _db.Orders
                .AsNoTracking()
                .Where(o => o.OrderStatus == OrderStatus.Paid &&
                            o.OrderOpenedAt >= lastWeekStart &&
                            o.OrderOpenedAt < lastWeekEnd)
                .Select(o => new { o.OrderOpenedAt, o.OrderTotalAmount })
                .ToListAsync();

            // 7 günlük vektörü oluştur (bugün = index 6)
            var weeklyTrend = Enumerable.Range(0, 7)
                .Select(i =>
                {
                    var dayLocal = todayLocal.AddDays(i - 6); // 6 gün önce → bugün

                    var dayUtcStart = dayLocal.ToUniversalTime();
                    var dayUtcEnd = dayLocal.AddDays(1).ToUniversalTime();

                    decimal tw = thisWeekOrders
                        .Where(o => o.OrderOpenedAt >= dayUtcStart &&
                                    o.OrderOpenedAt < dayUtcEnd)
                        .Sum(o => o.OrderTotalAmount);

                    // Geçen haftanın aynı haftanın günü (7 gün önce)
                    var prevDayUtcStart = dayUtcStart.AddDays(-7);
                    var prevDayUtcEnd = dayUtcEnd.AddDays(-7);

                    decimal lw = lastWeekOrders
                        .Where(o => o.OrderOpenedAt >= prevDayUtcStart &&
                                    o.OrderOpenedAt < prevDayUtcEnd)
                        .Sum(o => o.OrderTotalAmount);

                    return new WeeklyTrendPoint
                    {
                        DayLabel = TurkishDayNames[(int)dayLocal.DayOfWeek],
                        ThisWeek = tw,
                        LastWeek = lw
                    };
                })
                .ToList();

            // ════════════════════════════════════════════════════════════════
            //  7. STOK UYARILARI
            //     TrackStock=true ve silinmemiş ürünlerden
            //     AlertThreshold'ı aşanları çek; kritik önce sıralanır.
            // ════════════════════════════════════════════════════════════════

            var stockAlertItems = await _db.MenuItems
                .AsNoTracking()
                .Where(m => !m.IsDeleted &&
                             m.TrackStock &&
                             m.AlertThreshold > 0 &&
                             m.StockQuantity <= m.AlertThreshold)
                .OrderBy(m => m.StockQuantity)   // en kritik üstte
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
                    ? "critical"
                    : "low";

                return new StockAlertItem
                {
                    ProductName = m.MenuItemName,
                    CurrentQty = m.StockQuantity,
                    AlertThreshold = m.AlertThreshold,
                    Unit = "ad",   // Menü ürünleri için varsayılan birim
                    Level = level
                };
            }).ToList();

            // Düşük stok uyarısı sayısı (LowStockAlerts badge için)
            int lowStockCount = await _db.MenuItems
                .AsNoTracking()
                .CountAsync(m => !m.IsDeleted &&
                                  m.TrackStock &&
                                  m.AlertThreshold > 0 &&
                                  m.StockQuantity <= m.AlertThreshold);

            // ════════════════════════════════════════════════════════════════
            //  ViewModel'i Doldur ve Döndür
            // ════════════════════════════════════════════════════════════════

            var vm = new DashboardViewModel
            {
                // KPI
                DailyTotalRevenue = dailyRevenue,
                RevenueTrendPercentage = trendPct,
                YesterdayRevenue = yesterdayRevenue,
                TotalOrdersToday = totalOrders,
                ActiveOrdersNow = activeOrders,
                ClosedOrdersToday = closedOrders,
                AverageOrderValue = avgOrderValue,
                DailyRevenueTarget = 10_000m,  // İleride Settings tablosundan okunabilir

                // Masa
                TableStatus = tableStatus,
                TableHeatmap = heatmap,

                // Grafikler
                HourlyOrderTrends = hourlyTrends,
                TopSellingProducts = topProducts,
                WeeklyTrend = weeklyTrend,

                // Stok & uyarılar
                StockAlerts = stockAlerts,
                LowStockAlerts = lowStockCount,
                WaiterCallsActive = waiterCalls,
            };

            return View(vm);
        }
    }
}
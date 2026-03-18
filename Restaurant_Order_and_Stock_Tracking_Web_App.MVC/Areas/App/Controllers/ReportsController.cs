using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared.Common;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Reports;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.App.Controllers
{

    [Authorize(Roles = "Admin")]
    public class ReportsController : AppBaseController
    {
        private readonly RestaurantDbContext _context;

        // ── Sabitler ──────────────────────────────────────────────────────────
        // [ENUM] StatusOpen kaldırıldı — OrderStatus.Open kullanın
        // [ENUM] StatusPaid kaldırıldı — OrderStatus.Paid kullanın
        // [ENUM] StatusCancelled kaldırıldı — OrderStatus.Cancelled kullanın
        private const string MovementOut = "Çıkış";
        private const string MovementFix = "Düzeltme";

        private static readonly Dictionary<int, string> PaymentMethodNames = new()
        {
            { 0, "Nakit" },
            { 1, "Kredi Kartı" },
            { 2, "Banka Kartı" },
            { 3, "Diğer" }
        };

        public ReportsController(RestaurantDbContext context) => _context = context;

        // ── GET /Reports ─────────────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Raporlar";

            var todayLocal = DateTime.Today;
            var fromUtc = todayLocal.ToUniversalTime();
            var toUtc = todayLocal.AddDays(1).ToUniversalTime();

            // Bugünkü kapatılmış (paid) adisyonlar — tek sorgu
            var todayPaidOrders = await _context.Orders
                .AsNoTracking() // [PERF-05]
                .Where(o => o.OrderStatus == OrderStatus.Paid
                         && o.OrderClosedAt >= fromUtc
                         && o.OrderClosedAt < toUtc)
                .Select(o => new
                {
                    o.OrderId,
                    o.OrderTotalAmount,
                    o.OrderOpenedAt,
                    o.OrderClosedAt
                })
                .ToListAsync();

            var todayOrderIds = todayPaidOrders.Select(o => o.OrderId).ToList();

            // [P-03] Bugünkü tahsilat — ToListAsync+Sum yerine doğrudan SumAsync
            decimal todayNetCollected = await _context.Payments
                .AsNoTracking() // [PERF-05]
                .Where(p => p.PaymentsPaidAt >= fromUtc && p.PaymentsPaidAt < toUtc)
                .SumAsync(p => p.PaymentsAmount);

            // [P-03] Bugünkü brüt satış — ToListAsync+Sum yerine doğrudan SumAsync
            decimal todayGrossSales = await _context.Orders
                .AsNoTracking() // [PERF-05]
                .Where(o => o.OrderStatus == OrderStatus.Paid
                         && o.OrderClosedAt >= fromUtc
                         && o.OrderClosedAt < toUtc)
                .SumAsync(o => o.OrderTotalAmount);

            // Bugünkü en çok satan ürün + top 5
            var todayItems = await _context.OrderItems
                .AsNoTracking() // [PERF-05]
                .Where(oi => todayOrderIds.Contains(oi.OrderId) && oi.CancelledQuantity < oi.OrderItemQuantity)
                .Select(oi => new
                {
                    oi.MenuItemId,
                    ProductName = oi.MenuItem.MenuItemName,
                    ActiveQty = oi.OrderItemQuantity - oi.CancelledQuantity,
                    Revenue = oi.OrderItemLineTotal,
                    CategoryName = oi.MenuItem.Category.CategoryName
                })
                .ToListAsync();

            var topProducts = todayItems
                .GroupBy(x => new { x.MenuItemId, x.ProductName, x.CategoryName })
                .Select(g => new TopProductDto
                {
                    ProductName = g.Key.ProductName,
                    CategoryName = g.Key.CategoryName,
                    Quantity = g.Sum(x => x.ActiveQty),
                    Revenue = g.Sum(x => x.Revenue)
                })
                .OrderByDescending(x => x.Quantity)
                .Take(5)
                .ToList();

            // Bugünkü iptal tutarı
            var cancelledAmount = await _context.OrderItems
                .AsNoTracking() // [PERF-05]
                .Where(oi => oi.OrderItemAddedAt >= fromUtc
                          && oi.OrderItemAddedAt < toUtc
                          && oi.CancelledQuantity > 0)
                .SumAsync(oi => oi.CancelledQuantity * oi.OrderItemUnitPrice);

            // BUG 3 DÜZELTMESİ: Bugünkü fire tutarı — hem sipariş hem stok kaynaklı
            // Eski kod yalnızca OrderItems.IsWasted==true'yu topluyordu (eksik).
            // Yeni: StockLog üzerinden SourceType filtresiyle her iki kaynak birleştirildi.
            // Sipariş kaynaklı fire: SourceType="SiparişKaynaklı", Qty*UnitPrice
            // Stok kaynaklı fire:    SourceType="StokKaynaklı",    Qty*UnitPrice
            // [F-02] SourceType string → MovementCategory enum filtresi
            var orderWasteAmountToday = await _context.StockLogs
                .AsNoTracking() // [PERF-05]
                .Where(sl => sl.MovementCategory == MovementCategory.OrderWaste   // [F-02]
                          && sl.CreatedAt >= fromUtc
                          && sl.CreatedAt < toUtc)
                .SumAsync(sl => (decimal?)Math.Abs(sl.QuantityChange) * (sl.UnitPrice ?? sl.MenuItem.MenuItemPrice)) ?? 0m;

            var stockWasteAmountToday = await _context.StockLogs
                .AsNoTracking() // [PERF-05]
                .Where(sl => sl.MovementCategory == MovementCategory.StockWaste   // [F-02]
                          && sl.CreatedAt >= fromUtc
                          && sl.CreatedAt < toUtc)
                .SumAsync(sl => (decimal?)Math.Abs(sl.QuantityChange) * (sl.UnitPrice ?? sl.MenuItem.MenuItemPrice)) ?? 0m;

            var wasteAmount = orderWasteAmountToday + stockWasteAmountToday;


            // Kritik stok ürünleri
            var criticalItems = await _context.MenuItems
                .AsNoTracking() // [PERF-05]
                .Where(m => !m.IsDeleted
                         && m.TrackStock
                         // m.CriticalThreshold > 0 şartını >= 0 olarak değiştirdik
                         && m.CriticalThreshold >= 0
                         && m.StockQuantity <= m.CriticalThreshold)
                .Select(m => new CriticalStockItemDto
                {
                    ProductName = m.MenuItemName,
                    CurrentStock = m.StockQuantity,
                    CriticalThreshold = m.CriticalThreshold
                })
                .Take(5)
                .ToListAsync();

            // Saatlik satış (Dashboard mini grafik)
            var hourlySales = todayPaidOrders
                .GroupBy(o => o.OrderClosedAt!.Value.ToLocalTime().Hour)
                .Select(g => new HourlySalesDto { Hour = g.Key, Amount = g.Sum(x => x.OrderTotalAmount) })
                .OrderBy(x => x.Hour)
                .ToList();

            // Ortalama adisyon süresi
            var durations = todayPaidOrders
                .Where(o => o.OrderClosedAt.HasValue)
                .Select(o => (o.OrderClosedAt!.Value - o.OrderOpenedAt).TotalMinutes)
                .ToList();

            var vm = new ReportsDashboardViewModel
            {
                TodayGrossSales = todayGrossSales,   // [P-03] SumAsync
                TodayNetCollected = todayNetCollected,  // [P-03] SumAsync
                OpenOrderCount = await _context.Orders.CountAsync(o => o.OrderStatus == OrderStatus.Open),
                TopSellingItemToday = topProducts.FirstOrDefault()?.ProductName ?? "—",
                CriticalStockCount = await _context.MenuItems.CountAsync(m =>
                                            !m.IsDeleted && m.TrackStock
                                            && m.CriticalThreshold > 0
                                            && m.StockQuantity <= m.CriticalThreshold),
                TodayCancelledAmount = cancelledAmount,
                TodayWasteAmount = wasteAmount,
                AvgOrderDurationMinutes = durations.Count > 0 ? durations.Average() : 0,
                LastRefreshedAt = DateTime.Now,
                HourlySales = hourlySales,
                CriticalStockItems = criticalItems,
                TopProductsToday = topProducts
            };

            return View(vm);
        }

        // ── GET /Reports/Sales ───────────────────────────────────────────────
        public async Task<IActionResult> Sales(
            string preset = "today",
            DateTime? from = null,
            DateTime? to = null,
            bool includeCancelled = false)
        {
            ViewData["Title"] = "Satış Raporu";

            var filter = DateRangeFilter.FromPreset(preset, from, to, includeCancelled);
            var vm = new SalesReportViewModel { Filter = filter };

            await FillSalesViewModel(vm);
            return View(vm);
        }

        // ── GET /Reports/CancelAndWaste ──────────────────────────────────────
        public async Task<IActionResult> CancelAndWaste(
            string preset = "today",
            DateTime? from = null,
            DateTime? to = null)
        {
            ViewData["Title"] = "İptal & Fire Raporu";

            var filter = DateRangeFilter.FromPreset(preset, from, to);
            var fromUtc = filter.FromUtc;
            var toUtc = filter.ToUtc;

            // ══════════════════════════════════════════════════════════
            // BUG 1+4+5 DÜZELTMESİ — CancelAndWaste sorguları
            //
            // ESKI (hatalı):
            //   OrderWastes = OrderItems WHERE IsWasted==true
            //   → Quantity = OrderItemQty - CancelledQty = KALAN adet (0 oluyor!)
            //   → TotalLoss = OrderItemLineTotal = KALAN tutar (0 oluyor!)
            //   → Adisyon No bilgisi yok
            //   → Stok kaynaklı ile tip ayrımı yok (tüm Çıkış'lar birleşiyor)
            //
            // YENİ (doğru):
            //   Her iki kaynak StockLog.SourceType filtresiyle ayrışıyor.
            //   Qty   = Math.Abs(QuantityChange) → iptal edilen gerçek miktar
            //   Tutar = Qty * UnitPrice (OrderItem fiyatından kaydedildi)
            //   OrderId → raporda adisyon no gösterimi için
            // ══════════════════════════════════════════════════════════

            // 1. Sipariş Kaynaklı Fire: CancelItem'da IsWasted=true ile yazılan StockLog'lar
            var orderWastes = await _context.StockLogs
                .AsNoTracking() // [PERF-05]
                .Where(sl => sl.SourceType == "SiparişKaynaklı"
                          && sl.CreatedAt >= fromUtc
                          && sl.CreatedAt < toUtc)
                .Select(sl => new WasteItemDto
                {
                    ProductName = sl.MenuItem.MenuItemName,
                    Quantity = Math.Abs(sl.QuantityChange),          // iptal edilen gerçek miktar
                    UnitPrice = sl.UnitPrice ?? sl.MenuItem.MenuItemPrice,
                    CostPrice = sl.MenuItem.CostPrice,
                    TotalLoss = Math.Abs(sl.QuantityChange) * (sl.UnitPrice ?? sl.MenuItem.MenuItemPrice),
                    Date = sl.CreatedAt,
                    CancelReason = sl.Note ?? "",
                    SourceType = "SiparişKaynaklı",
                    OrderId = sl.OrderId                             // adisyon no raporda görünür
                })
                .OrderByDescending(x => x.Date)
                .ToListAsync();

            // 2. Stok Kaynaklı Fire: UpdateStock fire modundan gelen StockLog'lar
            var stockLogWastes = await _context.StockLogs
                .AsNoTracking() // [PERF-05]
                .Where(sl => sl.SourceType == "StokKaynaklı"
                          && sl.CreatedAt >= fromUtc
                          && sl.CreatedAt < toUtc)
                .Select(sl => new WasteItemDto
                {
                    ProductName = sl.MenuItem.MenuItemName,
                    Quantity = Math.Abs(sl.QuantityChange),
                    UnitPrice = sl.UnitPrice ?? sl.MenuItem.MenuItemPrice,
                    CostPrice = sl.MenuItem.CostPrice,
                    TotalLoss = Math.Abs(sl.QuantityChange) * (sl.UnitPrice ?? sl.MenuItem.MenuItemPrice),
                    Date = sl.CreatedAt,
                    Note = sl.Note ?? "",
                    SourceType = "StokKaynaklı",
                    OrderId = null                                   // adisyon bağlantısı yok
                })
                .OrderByDescending(x => x.Date)
                .ToListAsync();

            // IsWasted=false → stoka iade (refund)
            var refundedToStock = await _context.OrderItems
                .AsNoTracking() // [PERF-05]
                .Where(oi => oi.IsWasted == false
                          && oi.OrderItemAddedAt >= fromUtc
                          && oi.OrderItemAddedAt < toUtc
                          && oi.CancelledQuantity > 0)
                .SumAsync(oi => oi.CancelledQuantity * oi.OrderItemUnitPrice);

            var orderWasteTotal = orderWastes.Sum(x => x.TotalLoss);
            var stockLogWasteTotal = stockLogWastes.Sum(x => x.TotalLoss);

            // Top waste products (her iki kaynaktan birleştir)
            var allWasteItems = orderWastes.Concat(stockLogWastes).ToList();
            var topWaste = allWasteItems
                .GroupBy(x => x.ProductName)
                .Select(g => new TopWasteProductDto
                {
                    ProductName = g.Key,
                    TotalQuantity = g.Sum(x => x.Quantity),
                    TotalLoss = g.Sum(x => x.TotalLoss)
                })
                .OrderByDescending(x => x.TotalLoss)
                .Take(10)
                .ToList();

            var vm = new WasteReportViewModel
            {
                Filter = filter,
                OrderWastes = orderWastes,
                StockLogWastes = stockLogWastes,
                TotalWasteLoss = orderWasteTotal + stockLogWasteTotal,
                TotalWasteCount = allWasteItems.Sum(x => x.Quantity),
                TopWasteProducts = topWaste,
                TotalRefundedToStock = refundedToStock,
                OrderWasteTotal = orderWasteTotal,
                StockLogWasteTotal = stockLogWasteTotal
            };

            return View(vm);
        }

        // ── GET /Reports/Stock ───────────────────────────────────────────────
        public async Task<IActionResult> Stock(
            string preset = "month",
            DateTime? from = null,
            DateTime? to = null,
            string timeBase = "orderitem",
            string? category = null)
        {
            ViewData["Title"] = "Stok Tüketim Raporu";

            var filter = DateRangeFilter.FromPreset(preset, from, to, timeBase: timeBase);
            var fromUtc = filter.FromUtc;
            var toUtc = filter.ToUtc;
            int days = Math.Max(1, (int)(filter.To - filter.From).TotalDays);

            var items = await _context.MenuItems
                .AsNoTracking() // [PERF-05]
                .Where(m => !m.IsDeleted && m.TrackStock)
                .Include(m => m.Category)
                .ToListAsync();

            // Dönem tüketimini hesapla
            List<StockConsumptionDto> products;

            if (timeBase == "stocklog")
            {
                var logConsumption = await _context.StockLogs
                    .AsNoTracking() // [PERF-05]
                    .Where(sl => sl.CreatedAt >= fromUtc && sl.CreatedAt < toUtc
                              && (sl.MovementType == MovementOut || sl.MovementType == MovementFix))
                    .GroupBy(sl => sl.MenuItemId)
                    .Select(g => new { MenuItemId = g.Key, Consumed = g.Sum(sl => Math.Abs(sl.QuantityChange)) })
                    .ToDictionaryAsync(x => x.MenuItemId, x => x.Consumed);

                products = items.Select(m => new StockConsumptionDto
                {
                    MenuItemId = m.MenuItemId,
                    ProductName = m.MenuItemName,
                    CategoryName = m.Category?.CategoryName ?? "—",
                    CurrentStock = m.StockQuantity,
                    ConsumedInPeriod = logConsumption.TryGetValue(m.MenuItemId, out var c) ? c : 0,
                    DailyAvgConsumption = logConsumption.TryGetValue(m.MenuItemId, out var c2) ? (double)c2 / days : 0,
                    CostPrice = m.CostPrice
                }).ToList();
            }
            else
            {
                var orderConsumption = await _context.OrderItems
                    .AsNoTracking() // [PERF-05]
                    .Where(oi => oi.OrderItemAddedAt >= fromUtc && oi.OrderItemAddedAt < toUtc
                              && oi.OrderItemStatus != OrderItemStatus.Cancelled)
                    .GroupBy(oi => oi.MenuItemId)
                    .Select(g => new { MenuItemId = g.Key, Consumed = g.Sum(oi => oi.OrderItemQuantity - oi.CancelledQuantity) })
                    .ToDictionaryAsync(x => x.MenuItemId, x => x.Consumed);

                products = items.Select(m => new StockConsumptionDto
                {
                    MenuItemId = m.MenuItemId,
                    ProductName = m.MenuItemName,
                    CategoryName = m.Category?.CategoryName ?? "—",
                    CurrentStock = m.StockQuantity,
                    ConsumedInPeriod = orderConsumption.TryGetValue(m.MenuItemId, out var c) ? c : 0,
                    DailyAvgConsumption = orderConsumption.TryGetValue(m.MenuItemId, out var c2) ? (double)c2 / days : 0,
                    CostPrice = m.CostPrice
                }).ToList();
            }

            if (!string.IsNullOrEmpty(category))
                products = products.Where(p => p.CategoryName == category).ToList();

            products = products.OrderByDescending(p => p.ConsumedInPeriod).ToList();

            var vm = new StockReportViewModel
            {
                Filter = filter,
                Products = products,
                Categories = items.Select(m => m.Category?.CategoryName ?? "—").Distinct().OrderBy(x => x).ToList()
            };

            return View(vm);
        }

        // ── GET /Reports/Table ───────────────────────────────────────────────
        public async Task<IActionResult> Table(
            string preset = "week",
            DateTime? from = null,
            DateTime? to = null)
        {
            ViewData["Title"] = "Masa Performans Raporu";

            var filter = DateRangeFilter.FromPreset(preset, from, to);
            var fromUtc = filter.FromUtc;
            var toUtc = filter.ToUtc;

            var orders = await _context.Orders
                .AsNoTracking() // [PERF-05]
                .Where(o => o.OrderStatus == OrderStatus.Paid
                         && o.OrderClosedAt >= fromUtc
                         && o.OrderClosedAt < toUtc)
                .Include(o => o.Table)
                .Select(o => new
                {
                    o.TableId,
                    TableName = o.Table.TableName,
                    o.OrderTotalAmount,
                    o.OrderOpenedAt,
                    o.OrderClosedAt
                })
                .ToListAsync();

            var tablePerf = orders
                .GroupBy(o => new { o.TableId, o.TableName })
                .Select(g =>
                {
                    var durations = g
                        .Where(o => o.OrderClosedAt.HasValue)
                        .Select(o => (o.OrderClosedAt!.Value - o.OrderOpenedAt).TotalMinutes)
                        .ToList();

                    return new TablePerformanceDto
                    {
                        TableName = g.Key.TableName,
                        TotalOrders = g.Count(),
                        TotalRevenue = g.Sum(o => o.OrderTotalAmount),
                        AvgDurationMinutes = durations.Count > 0 ? durations.Average() : 0,
                        AvgOrderValue = g.Count() > 0 ? g.Sum(o => o.OrderTotalAmount) / g.Count() : 0
                    };
                })
                .OrderByDescending(t => t.TotalRevenue)
                .ToList();

            // En yoğun saat
            var busiestHour = orders.Count > 0
                ? orders.GroupBy(o => o.OrderOpenedAt.ToLocalTime().Hour)
                        .OrderByDescending(g => g.Count())
                        .First().Key
                : 0;

            var vm = new TableReportViewModel
            {
                Filter = filter,
                Tables = tablePerf,
                BusiestHour = busiestHour,
                BusiestTable = tablePerf.FirstOrDefault()?.TableName ?? "—",
                TotalOrders = orders.Count,
                AvgDuration = orders.Count > 0
                    ? orders.Where(o => o.OrderClosedAt.HasValue)
                             .Select(o => (o.OrderClosedAt!.Value - o.OrderOpenedAt).TotalMinutes)
                             .DefaultIfEmpty(0)
                             .Average()
                    : 0
            };

            return View(vm);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  AJAX JSON ENDPOINTS
        // ═════════════════════════════════════════════════════════════════════

        [HttpGet]
        public async Task<IActionResult> GetSalesChartData(
            string preset = "today",
            DateTime? from = null,
            DateTime? to = null,
            bool includeCancelled = false)
        {
            var filter = DateRangeFilter.FromPreset(preset, from, to, includeCancelled);
            var fromUtc = filter.FromUtc;
            var toUtc = filter.ToUtc;
            bool isToday = preset == "today";

            OrderStatus[] statuses = includeCancelled
                ? new[] { OrderStatus.Paid, OrderStatus.Cancelled }
                : new[] { OrderStatus.Paid };

            var orders = await _context.Orders
                .AsNoTracking() // [PERF-05]
                .Where(o => statuses.Contains(o.OrderStatus)
                         && o.OrderClosedAt >= fromUtc
                         && o.OrderClosedAt < toUtc)
                .Select(o => new { o.OrderTotalAmount, o.OrderClosedAt })
                .ToListAsync();

            var payments = await _context.Payments
                .AsNoTracking() // [PERF-05]
                .Where(p => p.PaymentsPaidAt >= fromUtc && p.PaymentsPaidAt < toUtc)
                .Select(p => new { p.PaymentsAmount, p.PaymentsPaidAt })
                .ToListAsync();

            if (isToday)
            {
                var labels = Enumerable.Range(0, 24).Select(h => $"{h:D2}:00").ToList();
                var grossData = Enumerable.Range(0, 24)
                    .Select(h => orders.Where(o => o.OrderClosedAt!.Value.ToLocalTime().Hour == h)
                                       .Sum(o => o.OrderTotalAmount))
                    .ToList();
                var netData = Enumerable.Range(0, 24)
                    .Select(h => payments.Where(p => p.PaymentsPaidAt.ToLocalTime().Hour == h)
                                         .Sum(p => p.PaymentsAmount))
                    .ToList();
                // ── FIX: Widget toplamları — filtre değiştiğinde 4 kart da güncellenir ──
                // [P-03] Series data için orders/payments bellekte — scalar toplamlar buradan
                var totalGross = orders.Sum(o => o.OrderTotalAmount);
                var totalCollected = payments.Sum(p => p.PaymentsAmount);
                var orderCount = orders.Count;
                return Json(new
                {
                    labels,
                    grossData,
                    netData,
                    totalGross,
                    totalCollected,
                    difference = totalGross - totalCollected,
                    orderCount
                });
            }
            else
            {
                int days = (int)(filter.To - filter.From).TotalDays;
                var labels = Enumerable.Range(0, days)
                    .Select(i => filter.From.AddDays(i).ToString("dd.MM"))
                    .ToList();
                var grossData = Enumerable.Range(0, days)
                    .Select(i =>
                    {
                        var d = filter.From.AddDays(i).ToUniversalTime();
                        var d2 = d.AddDays(1);
                        return orders.Where(o => o.OrderClosedAt >= d && o.OrderClosedAt < d2)
                                     .Sum(o => o.OrderTotalAmount);
                    }).ToList();
                var netData = Enumerable.Range(0, days)
                    .Select(i =>
                    {
                        var d = filter.From.AddDays(i).ToUniversalTime();
                        var d2 = d.AddDays(1);
                        return payments.Where(p => p.PaymentsPaidAt >= d && p.PaymentsPaidAt < d2)
                                       .Sum(p => p.PaymentsAmount);
                    }).ToList();
                // ── FIX: Widget toplamları — [P-03] ──
                var totalGross = orders.Sum(o => o.OrderTotalAmount);
                var totalCollected = payments.Sum(p => p.PaymentsAmount);
                var orderCount = orders.Count;
                return Json(new
                {
                    labels,
                    grossData,
                    netData,
                    totalGross,
                    totalCollected,
                    difference = totalGross - totalCollected,
                    orderCount
                });
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetPaymentChartData(
            string preset = "today",
            DateTime? from = null,
            DateTime? to = null)
        {
            var filter = DateRangeFilter.FromPreset(preset, from, to);
            var fromUtc = filter.FromUtc;
            var toUtc = filter.ToUtc;

            var payments = await _context.Payments
                .AsNoTracking() // [PERF-05]
                .Where(p => p.PaymentsPaidAt >= fromUtc && p.PaymentsPaidAt < toUtc)
                .GroupBy(p => p.PaymentsMethod)
                .Select(g => new { Method = g.Key, Total = g.Sum(p => p.PaymentsAmount), Count = g.Count() })
                .ToListAsync();

            var grandTotal = payments.Sum(p => p.Total);
            var labels = payments.Select(p => PaymentMethodNames.TryGetValue(p.Method, out var n) ? n : $"Yöntem {p.Method}").ToList();
            var amounts = payments.Select(p => p.Total).ToList();
            var counts = payments.Select(p => p.Count).ToList();
            var pcts = payments.Select(p => grandTotal > 0 ? Math.Round((double)(p.Total / grandTotal) * 100, 1) : 0).ToList();

            return Json(new { labels, amounts, counts, percentages = pcts });
        }

        [HttpGet]
        public async Task<IActionResult> GetTopProductsData(
            string preset = "today",
            DateTime? from = null,
            DateTime? to = null,
            int top = 10)
        {
            var filter = DateRangeFilter.FromPreset(preset, from, to);
            var fromUtc = filter.FromUtc;
            var toUtc = filter.ToUtc;

            var result = await _context.OrderItems
                .AsNoTracking() // [PERF-05]
                .Where(oi => oi.OrderItemAddedAt >= fromUtc
                          && oi.OrderItemAddedAt < toUtc
                          && oi.OrderItemStatus != OrderItemStatus.Cancelled)
                .GroupBy(oi => new { oi.MenuItemId, oi.MenuItem.MenuItemName })
                .Select(g => new
                {
                    Name = g.Key.MenuItemName,
                    Quantity = g.Sum(x => x.OrderItemQuantity - x.CancelledQuantity),
                    Revenue = g.Sum(x => x.OrderItemLineTotal)
                })
                .OrderByDescending(x => x.Quantity)
                .Take(top)
                .ToListAsync();

            return Json(new
            {
                labels = result.Select(x => x.Name).ToList(),
                quantities = result.Select(x => x.Quantity).ToList(),
                revenues = result.Select(x => x.Revenue).ToList()
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetCategorySalesData(
            string preset = "today",
            DateTime? from = null,
            DateTime? to = null)
        {
            var filter = DateRangeFilter.FromPreset(preset, from, to);
            var fromUtc = filter.FromUtc;
            var toUtc = filter.ToUtc;

            var result = await _context.OrderItems
                .AsNoTracking() // [PERF-05]
                .Where(oi => oi.OrderItemAddedAt >= fromUtc
                          && oi.OrderItemAddedAt < toUtc
                          && oi.OrderItemStatus != OrderItemStatus.Cancelled)
                .GroupBy(oi => oi.MenuItem.Category.CategoryName)
                .Select(g => new { Category = g.Key, Revenue = g.Sum(x => x.OrderItemLineTotal) })
                .ToListAsync();

            var total = result.Sum(x => x.Revenue);
            var pcts = result.Select(x => total > 0 ? Math.Round((double)(x.Revenue / total) * 100, 1) : 0).ToList();

            return Json(new
            {
                labels = result.Select(x => x.Category ?? "Kategorisiz").ToList(),
                amounts = result.Select(x => x.Revenue).ToList(),
                percentages = pcts
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetWasteChartData(
            string preset = "today",
            DateTime? from = null,
            DateTime? to = null)
        {
            var filter = DateRangeFilter.FromPreset(preset, from, to);
            var fromUtc = filter.FromUtc;
            var toUtc = filter.ToUtc;

            // BUG 3 DÜZELTMESİ — GetWasteChartData: SourceType bazlı sorgular
            var orderWasteTotal = await _context.StockLogs
                .AsNoTracking() // [PERF-05]
                .Where(sl => sl.SourceType == "SiparişKaynaklı"
                          && sl.CreatedAt >= fromUtc
                          && sl.CreatedAt < toUtc)
                .SumAsync(sl => (decimal?)Math.Abs(sl.QuantityChange) * (sl.UnitPrice ?? sl.MenuItem.MenuItemPrice)) ?? 0m;

            var stockLogWasteTotal = await _context.StockLogs
                .AsNoTracking() // [PERF-05]
                .Where(sl => sl.SourceType == "StokKaynaklı"
                          && sl.CreatedAt >= fromUtc
                          && sl.CreatedAt < toUtc)
                .SumAsync(sl => (decimal?)Math.Abs(sl.QuantityChange) * (sl.UnitPrice ?? sl.MenuItem.MenuItemPrice)) ?? 0m;

            var topProducts = await _context.StockLogs
                .AsNoTracking() // [PERF-05]
                .Where(sl => (sl.SourceType == "SiparişKaynaklı" || sl.SourceType == "StokKaynaklı")
                          && sl.CreatedAt >= fromUtc
                          && sl.CreatedAt < toUtc)
                .GroupBy(sl => sl.MenuItem.MenuItemName)
                .Select(g => new
                {
                    Name = g.Key,
                    Loss = g.Sum(x => (decimal?)Math.Abs(x.QuantityChange) * (x.UnitPrice ?? x.MenuItem.MenuItemPrice)) ?? 0m
                })
                .OrderByDescending(x => x.Loss)
                .Take(10)
                .ToListAsync();

            // FIX: Widget toplamları — filtre değiştiğinde 3 kart da AJAX ile güncellenir
            var totalWasteLoss = orderWasteTotal + stockLogWasteTotal;

            return Json(new
            {
                // Grafik dizileri (değişmedi)
                orderWasteTotal,
                stockLogWasteTotal,
                topProducts = topProducts.Select(x => new { x.Name, x.Loss }).ToList(),
                // Yeni: widget'lar için özet toplamlar
                totalWasteLoss
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetStockTrendData(int menuItemId, int days = 30)
        {
            var fromUtc = DateTime.UtcNow.AddDays(-days);

            var logs = await _context.StockLogs
                .AsNoTracking() // [PERF-05]
                .Where(sl => sl.MenuItemId == menuItemId && sl.CreatedAt >= fromUtc)
                .OrderBy(sl => sl.CreatedAt)
                .Select(sl => new { sl.CreatedAt, sl.NewStock })
                .ToListAsync();

            return Json(new
            {
                labels = logs.Select(l => l.CreatedAt.ToLocalTime().ToString("dd.MM")).ToList(),
                stocks = logs.Select(l => l.NewStock).ToList()
            });
        }

        // ═════════════════════════════════════════════════════════════════════
        //  EXPORT ENDPOINTS
        // ═════════════════════════════════════════════════════════════════════

        [HttpGet]
        public async Task<IActionResult> ExportCsv(
            string type = "sales",
            string preset = "today",
            DateTime? from = null,
            DateTime? to = null,
            bool includeCancelled = false)
        {
            var filter = DateRangeFilter.FromPreset(preset, from, to, includeCancelled);
            var sb = new StringBuilder();
            var bom = "\uFEFF"; // UTF-8 BOM

            switch (type)
            {
                case "sales":
                    sb.AppendLine("Tarih,Adisyon No,Masa,Brüt Tutar,Tahsilat,Durum");
                    var salesOrders = await GetSalesOrdersForExport(filter);
                    foreach (var o in salesOrders)
                        sb.AppendLine($"{o.Date:dd.MM.yyyy HH:mm},{o.OrderId},{o.TableName},{o.GrossAmount:F2},{o.NetAmount:F2},{o.Status}");
                    break;

                case "waste":
                    sb.AppendLine("Tarih,Ürün,Miktar,Birim Fiyat,Toplam Kayıp,Kaynak,Not");
                    var wasteItems = await GetWasteItemsForExport(filter);
                    foreach (var w in wasteItems)
                        sb.AppendLine($"{w.Date:dd.MM.yyyy HH:mm},{Escape(w.ProductName)},{w.Quantity},{w.UnitPrice:F2},{w.TotalLoss:F2},{w.Source},{Escape(w.Note)}");
                    break;

                case "stock":
                    sb.AppendLine("Ürün,Kategori,Güncel Stok,Dönem Tüketimi,Günlük Ort.,Tahmini Tükenme (gün),Maliyet Fiyatı");
                    var stockItems = await GetStockItemsForExport(filter);
                    foreach (var s in stockItems)
                        sb.AppendLine($"{Escape(s.ProductName)},{Escape(s.CategoryName)},{s.CurrentStock},{s.ConsumedInPeriod},{s.DailyAvgConsumption:F2},{s.EstimatedDaysLeft},{(s.CostPrice.HasValue ? s.CostPrice.Value.ToString("F2") : "—")}");
                    break;

                case "table":
                    sb.AppendLine("Masa,Adisyon Sayısı,Toplam Ciro,Ort. Süre (dk),Ort. Sepet");
                    var tableItems = await GetTableItemsForExport(filter);
                    foreach (var t in tableItems)
                        sb.AppendLine($"{Escape(t.TableName)},{t.TotalOrders},{t.TotalRevenue:F2},{t.AvgDurationMinutes:F0},{t.AvgOrderValue:F2}");
                    break;
            }

            var bytes = Encoding.UTF8.GetBytes(bom + sb.ToString());
            return File(bytes, "text/csv;charset=utf-8", $"{type}_raporu_{DateTime.Today:yyyy-MM-dd}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> ExportPdf(
    string type = "sales",
    string preset = "today",
    DateTime? from = null,
    DateTime? to = null)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var filter = DateRangeFilter.FromPreset(preset, from, to);
            var title = type switch
            {
                "sales" => "Satış Raporu",
                "waste" => "Fire & İptal Raporu",
                "stock" => "Stok Tüketim Raporu",
                "table" => "Masa Performans Raporu",
                _ => "Rapor"
            };

            // ── Veriyi burada (async context'te) çek, lambda'ya hazır geçir ──
            var salesRows = type == "sales" ? await GetSalesOrdersForExport(filter) : null;
            var stockRows = type == "stock" ? await GetStockItemsForExport(filter) : null;
            var wasteRows = type == "waste" ? await GetWasteItemsForExport(filter) : null;
            var tableRows = type == "table" ? await GetTableItemsForExport(filter) : null;

            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30, Unit.Point);
                    page.DefaultTextStyle(t => t.FontSize(10).FontFamily("Arial"));

                    page.Header().Column(col =>
                    {
                        col.Item().Text($"RestaurantOS — {title}")
                           .SemiBold().FontSize(16);
                        col.Item().Text($"Dönem: {filter.DisplayRange}")
                           .FontSize(10).FontColor(Colors.Grey.Medium);
                        col.Item().Text($"Oluşturulma: {DateTime.Now:dd.MM.yyyy HH:mm}")
                           .FontSize(9).FontColor(Colors.Grey.Lighten1);
                        col.Item().PaddingTop(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    });

                    page.Content().PaddingTop(12).Column(col =>   // artık async değil
                    {
                        switch (type)
                        {
                            case "sales":
                                var sr = salesRows ?? new List<(int, DateTime, string, decimal, decimal, string)>();
                                if (!sr.Any())
                                {
                                    col.Item().Text("Bu dönemde satış verisi bulunamadı.")
                                       .FontColor(Colors.Grey.Medium);
                                    break;
                                }
                                col.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn(2); c.RelativeColumn(1); c.RelativeColumn(2);
                                        c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(1);
                                    });
                                    t.Header(h =>
                                    {
                                        foreach (var hdr in new[] { "Tarih", "No", "Masa", "Brüt", "Tahsilat", "Durum" })
                                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(hdr).SemiBold();
                                    });
                                    foreach (var r in sr)
                                    {
                                        t.Cell().Padding(3).Text(r.Item2.ToString("dd.MM.yy HH:mm"));
                                        t.Cell().Padding(3).Text(r.Item1.ToString());
                                        t.Cell().Padding(3).Text(r.Item3 ?? "—");
                                        t.Cell().Padding(3).Text($"₺{r.Item4:F2}");
                                        t.Cell().Padding(3).Text($"₺{r.Item5:F2}");
                                        t.Cell().Padding(3).Text(r.Item6 ?? "—");
                                    }
                                });
                                break;

                            case "stock":
                                var stk = stockRows ?? new List<StockConsumptionDto>();
                                if (!stk.Any())
                                {
                                    col.Item().Text("Bu dönemde stok verisi bulunamadı.")
                                       .FontColor(Colors.Grey.Medium);
                                    break;
                                }
                                col.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn(3); c.RelativeColumn(2); c.RelativeColumn(1);
                                        c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(1);
                                    });
                                    t.Header(h =>
                                    {
                                        foreach (var hdr in new[] { "Ürün", "Kategori", "Stok", "Tüketim", "Günlük Ort.", "Tükenme" })
                                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(hdr).SemiBold();
                                    });
                                    foreach (var r in stk)
                                    {
                                        t.Cell().Padding(3).Text(r.ProductName ?? "—");
                                        t.Cell().Padding(3).Text(r.CategoryName ?? "—");
                                        t.Cell().Padding(3).Text(r.CurrentStock.ToString());
                                        t.Cell().Padding(3).Text(r.ConsumedInPeriod.ToString());
                                        t.Cell().Padding(3).Text($"{r.DailyAvgConsumption:F1}");
                                        t.Cell().Padding(3).Text(r.EstimatedDaysLeft == 999 ? "∞" : r.EstimatedDaysLeft.ToString());
                                    }
                                });
                                break;

                            case "waste":
                                var wr = wasteRows ?? new List<(DateTime, string, int, decimal, decimal, string, string)>();
                                if (!wr.Any())
                                {
                                    col.Item().Text("Bu dönemde fire verisi bulunamadı.")
                                       .FontColor(Colors.Grey.Medium);
                                    break;
                                }
                                col.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn(2); c.RelativeColumn(3); c.RelativeColumn(1);
                                        c.RelativeColumn(2); c.RelativeColumn(2); c.RelativeColumn(2);
                                    });
                                    t.Header(h =>
                                    {
                                        foreach (var hdr in new[] { "Tarih", "Ürün", "Miktar", "Birim Fiyat", "Toplam Kayıp", "Kaynak" })
                                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(hdr).SemiBold();
                                    });
                                    foreach (var r in wr)
                                    {
                                        t.Cell().Padding(3).Text(r.Item1.ToString("dd.MM.yy HH:mm"));
                                        t.Cell().Padding(3).Text(r.Item2 ?? "—");
                                        t.Cell().Padding(3).Text(r.Item3.ToString());
                                        t.Cell().Padding(3).Text($"₺{r.Item4:F2}");
                                        t.Cell().Padding(3).Text($"₺{r.Item5:F2}");
                                        t.Cell().Padding(3).Text(r.Item6 ?? "—");
                                    }
                                });
                                break;

                            case "table":
                                var tr = tableRows ?? new List<TablePerformanceDto>();
                                if (!tr.Any())
                                {
                                    col.Item().Text("Bu dönemde masa verisi bulunamadı.")
                                       .FontColor(Colors.Grey.Medium);
                                    break;
                                }
                                col.Item().Table(t =>
                                {
                                    t.ColumnsDefinition(c =>
                                    {
                                        c.RelativeColumn(3); c.RelativeColumn(1); c.RelativeColumn(2);
                                        c.RelativeColumn(2); c.RelativeColumn(2);
                                    });
                                    t.Header(h =>
                                    {
                                        foreach (var hdr in new[] { "Masa", "Adisyon", "Toplam Ciro", "Ort. Süre", "Ort. Sepet" })
                                            h.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(hdr).SemiBold();
                                    });
                                    foreach (var r in tr)
                                    {
                                        t.Cell().Padding(3).Text(r.TableName ?? "—");
                                        t.Cell().Padding(3).Text(r.TotalOrders.ToString());
                                        t.Cell().Padding(3).Text($"₺{r.TotalRevenue:F2}");
                                        t.Cell().Padding(3).Text($"{r.AvgDurationMinutes:F0} dk");
                                        t.Cell().Padding(3).Text($"₺{r.AvgOrderValue:F2}");
                                    }
                                });
                                break;

                            default:
                                col.Item().Text("Bu rapor türü için PDF desteği yakında eklenecek.")
                                   .FontColor(Colors.Grey.Medium);
                                break;
                        }
                    });

                    page.Footer().AlignRight().Text(t =>
                    {
                        t.Span("Sayfa ").FontSize(8).FontColor(Colors.Grey.Medium);
                        t.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                    });
                });
            }).GeneratePdf();

            return File(pdfBytes, "application/pdf", $"{type}_raporu_{DateTime.Today:yyyy-MM-dd}.pdf");
        }

        // ═════════════════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ═════════════════════════════════════════════════════════════════════

        private async Task FillSalesViewModel(SalesReportViewModel vm)
        {
            var fromUtc = vm.Filter.FromUtc;
            var toUtc = vm.Filter.ToUtc;

            OrderStatus[] statuses = vm.Filter.IncludeCancelled
                ? new[] { OrderStatus.Paid, OrderStatus.Cancelled }
                : new[] { OrderStatus.Paid };

            var orders = await _context.Orders
                .AsNoTracking() // [PERF-05]
                .Where(o => statuses.Contains(o.OrderStatus)
                         && o.OrderClosedAt >= fromUtc
                         && o.OrderClosedAt < toUtc)
                .Select(o => new { o.OrderId, o.OrderTotalAmount, o.OrderStatus, o.OrderClosedAt })
                .ToListAsync();

            var orderIds = orders.Select(o => o.OrderId).ToList();

            var payments = await _context.Payments
                .AsNoTracking() // [PERF-05]
                .Where(p => orderIds.Contains(p.OrderId))
                .GroupBy(p => p.PaymentsMethod)
                .Select(g => new { Method = g.Key, Total = g.Sum(p => p.PaymentsAmount), Count = g.Count() })
                .ToListAsync();

            var totalPayments = payments.Sum(p => p.Total);

            var topProducts = await _context.OrderItems
                .AsNoTracking() // [PERF-05]
                .Where(oi => orderIds.Contains(oi.OrderId) && oi.OrderItemStatus != OrderItemStatus.Cancelled)
                .GroupBy(oi => new { oi.MenuItemId, oi.MenuItem.MenuItemName, oi.MenuItem.Category.CategoryName })
                .Select(g => new TopProductDto
                {
                    ProductName = g.Key.MenuItemName,
                    CategoryName = g.Key.CategoryName,
                    Quantity = g.Sum(x => x.OrderItemQuantity - x.CancelledQuantity),
                    Revenue = g.Sum(x => x.OrderItemLineTotal)
                })
                .OrderByDescending(x => x.Quantity)
                .Take(10)
                .ToListAsync();

            var catSales = await _context.OrderItems
                .AsNoTracking() // [PERF-05]
                .Where(oi => orderIds.Contains(oi.OrderId) && oi.OrderItemStatus != OrderItemStatus.Cancelled)
                .GroupBy(oi => oi.MenuItem.Category.CategoryName)
                .Select(g => new { Category = g.Key, Revenue = g.Sum(x => x.OrderItemLineTotal) })
                .ToListAsync();

            var catTotal = catSales.Sum(x => x.Revenue);

            vm.GrossSales = orders.Sum(o => o.OrderTotalAmount);
            vm.NetCollected = totalPayments;
            vm.TotalOrderCount = orders.Count;
            vm.CancelledOrderCount = orders.Count(o => o.OrderStatus == OrderStatus.Cancelled);
            vm.PaymentBreakdown = payments.Select(p => new PaymentMethodDto
            {
                MethodName = PaymentMethodNames.TryGetValue(p.Method, out var n) ? n : $"Yöntem {p.Method}",
                TotalAmount = p.Total,
                TransactionCount = p.Count,
                Percentage = totalPayments > 0 ? Math.Round((double)(p.Total / totalPayments) * 100, 1) : 0
            }).ToList();
            vm.TopProducts = topProducts;
            vm.CategorySales = catSales.Select(c => new CategorySalesDto
            {
                CategoryName = c.Category ?? "Kategorisiz",
                Revenue = c.Revenue,
                Percentage = catTotal > 0 ? Math.Round((double)(c.Revenue / catTotal) * 100, 1) : 0
            }).OrderByDescending(x => x.Revenue).ToList();
        }

        private async Task<List<(int OrderId, DateTime Date, string TableName, decimal GrossAmount, decimal NetAmount, string Status)>>
            GetSalesOrdersForExport(DateRangeFilter filter)
        {
            var fromUtc = filter.FromUtc;
            var toUtc = filter.ToUtc;

            var orders = await _context.Orders
                .AsNoTracking() // [PERF-05]
                .Where(o => (o.OrderStatus == OrderStatus.Paid || (filter.IncludeCancelled && o.OrderStatus == OrderStatus.Cancelled))
                         && o.OrderClosedAt >= fromUtc
                         && o.OrderClosedAt < toUtc)
                .Include(o => o.Table)
                .Include(o => o.Payments)
                .Select(o => new
                {
                    o.OrderId,
                    Date = o.OrderClosedAt ?? o.OrderOpenedAt,
                    TableName = o.Table.TableName,
                    Gross = o.OrderTotalAmount,
                    Net = o.Payments.Sum(p => p.PaymentsAmount),
                    o.OrderStatus
                })
                .OrderByDescending(o => o.Date)
                .ToListAsync();

            return orders.Select(o => (
                o.OrderId,
                o.Date.ToLocalTime(),
                o.TableName,
                o.Gross,
                o.Net,
                o.OrderStatus.ToString() // <--- BURASI DÜZELTİLDİ: .ToString() eklendi
            )).ToList();
        }

        private async Task<List<(DateTime Date, string ProductName, int Quantity, decimal UnitPrice, decimal TotalLoss, string Source, string Note)>>
            GetWasteItemsForExport(DateRangeFilter filter)
        {
            var fromUtc = filter.FromUtc;
            var toUtc = filter.ToUtc;

            // BUG 5 DÜZELTMESİ — Export: SourceType bazlı, doğru miktar/tutar
            var orderWaste = await _context.StockLogs
                .AsNoTracking() // [PERF-05]
                .Where(sl => sl.SourceType == "SiparişKaynaklı"
                          && sl.CreatedAt >= fromUtc && sl.CreatedAt < toUtc)
                .Select(sl => new
                {
                    Date = sl.CreatedAt,
                    ProductName = sl.MenuItem.MenuItemName,
                    Quantity = Math.Abs(sl.QuantityChange),
                    UnitPrice = sl.UnitPrice ?? sl.MenuItem.MenuItemPrice,
                    TotalLoss = Math.Abs(sl.QuantityChange) * (sl.UnitPrice ?? sl.MenuItem.MenuItemPrice),
                    Note = sl.Note ?? ""
                })
                .ToListAsync();

            var stockWaste = await _context.StockLogs
                .AsNoTracking() // [PERF-05]
                .Where(sl => sl.SourceType == "StokKaynaklı"
                          && sl.CreatedAt >= fromUtc && sl.CreatedAt < toUtc)
                .Select(sl => new
                {
                    Date = sl.CreatedAt,
                    ProductName = sl.MenuItem.MenuItemName,
                    Quantity = Math.Abs(sl.QuantityChange),
                    UnitPrice = sl.UnitPrice ?? sl.MenuItem.MenuItemPrice,
                    TotalLoss = Math.Abs(sl.QuantityChange) * (sl.UnitPrice ?? sl.MenuItem.MenuItemPrice),
                    Note = sl.Note ?? ""
                })
                .ToListAsync();

            return orderWaste.Select(x => (x.Date.ToLocalTime(), x.ProductName, x.Quantity, x.UnitPrice, x.TotalLoss, "Sipariş Kaynaklı", x.Note))
                .Concat(stockWaste.Select(x => (x.Date.ToLocalTime(), x.ProductName, x.Quantity, x.UnitPrice, x.TotalLoss, "Stok Kaynaklı", x.Note)))
                .OrderByDescending(x => x.Item1)
                .ToList();
        }

        private async Task<List<StockConsumptionDto>> GetStockItemsForExport(DateRangeFilter filter)
        {
            var fromUtc = filter.FromUtc;
            var toUtc = filter.ToUtc;
            int days = Math.Max(1, (int)(filter.To - filter.From).TotalDays);

            var items = await _context.MenuItems
                .AsNoTracking() // [PERF-05]
                .Where(m => !m.IsDeleted && m.TrackStock)
                .Include(m => m.Category)
                .ToListAsync();

            var consumption = await _context.OrderItems
                .AsNoTracking() // [PERF-05]
                .Where(oi => oi.OrderItemAddedAt >= fromUtc && oi.OrderItemAddedAt < toUtc
                          && oi.OrderItemStatus != OrderItemStatus.Cancelled)
                .GroupBy(oi => oi.MenuItemId)
                .Select(g => new { MenuItemId = g.Key, Consumed = g.Sum(oi => oi.OrderItemQuantity - oi.CancelledQuantity) })
                .ToDictionaryAsync(x => x.MenuItemId, x => x.Consumed);

            return items.Select(m => new StockConsumptionDto
            {
                MenuItemId = m.MenuItemId,
                ProductName = m.MenuItemName,
                CategoryName = m.Category?.CategoryName ?? "—",
                CurrentStock = m.StockQuantity,
                ConsumedInPeriod = consumption.TryGetValue(m.MenuItemId, out var c) ? c : 0,
                DailyAvgConsumption = consumption.TryGetValue(m.MenuItemId, out var c2) ? (double)c2 / days : 0,
                CostPrice = m.CostPrice
            }).OrderByDescending(x => x.ConsumedInPeriod).ToList();
        }

        private async Task<List<TablePerformanceDto>> GetTableItemsForExport(DateRangeFilter filter)
        {
            var fromUtc = filter.FromUtc;
            var toUtc = filter.ToUtc;

            var orders = await _context.Orders
                .AsNoTracking() // [PERF-05]
                .Where(o => o.OrderStatus == OrderStatus.Paid && o.OrderClosedAt >= fromUtc && o.OrderClosedAt < toUtc)
                .Include(o => o.Table)
                .Select(o => new { o.Table.TableName, o.OrderTotalAmount, o.OrderOpenedAt, o.OrderClosedAt })
                .ToListAsync();

            return orders.GroupBy(o => o.TableName)
                .Select(g =>
                {
                    var durations = g.Where(o => o.OrderClosedAt.HasValue)
                                    .Select(o => (o.OrderClosedAt!.Value - o.OrderOpenedAt).TotalMinutes).ToList();
                    return new TablePerformanceDto
                    {
                        TableName = g.Key,
                        TotalOrders = g.Count(),
                        TotalRevenue = g.Sum(o => o.OrderTotalAmount),
                        AvgDurationMinutes = durations.Count > 0 ? durations.Average() : 0,
                        AvgOrderValue = g.Count() > 0 ? g.Sum(o => o.OrderTotalAmount) / g.Count() : 0
                    };
                })
                .OrderByDescending(t => t.TotalRevenue)
                .ToList();
        }

        private static string Escape(string? s) => $"\"{(s ?? "").Replace("\"", "\"\"")}\"";
    }
}
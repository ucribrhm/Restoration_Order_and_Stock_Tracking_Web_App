// ════════════════════════════════════════════════════════════════════════════
//  ViewModels/Dashboard/DashboardViewModel.cs
//  Yol: Restaurant_Order_and_Stock_Tracking_Web_App.MVC/ViewModels/Dashboard/
//
//  ⚡ Mevcut Index.cshtml ile TAM UYUMLU.
//     Mock versiyonuna göre eklenen yeni alanlar: WeeklyTrend, StockAlerts,
//     TableHeatmap, YesterdayRevenue, ClosedOrdersToday.
//     Hiçbir mevcut alan kaldırılmadı; Razor view'da değişiklik gerekmez.
// ════════════════════════════════════════════════════════════════════════════

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Dashboard
{
    /// <summary>
    /// Dashboard için tüm KPI, grafik ve uyarı verilerini taşıyan ViewModel.
    /// </summary>
    public class DashboardViewModel
    {
        // ── KPI KARTLARI ─────────────────────────────────────────────────────

        /// <summary>Bugünkü toplam ciro — "paid" statüsündeki siparişler.</summary>
        public decimal DailyTotalRevenue { get; set; }

        /// <summary>
        /// Düne göre ciro değişim yüzdesi.
        /// +12.4 → %12.4 artış | -5.2 → %5.2 düşüş | null → dün hiç veri yok
        /// </summary>
        public decimal? RevenueTrendPercentage { get; set; }

        /// <summary>Dünkü toplam ciro (trend badge alt metni için).</summary>
        public decimal YesterdayRevenue { get; set; }

        /// <summary>Bugün açılan toplam sipariş adedi (open + paid).</summary>
        public int TotalOrdersToday { get; set; }

        /// <summary>Şu an "open" durumundaki sipariş sayısı.</summary>
        public int ActiveOrdersNow { get; set; }

        /// <summary>Bugün "paid" durumuna geçen (kapanan) sipariş sayısı.</summary>
        public int ClosedOrdersToday { get; set; }

        /// <summary>Bugünkü adisyon ortalaması — yalnızca paid siparişler.</summary>
        public decimal AverageOrderValue { get; set; }

        // ── HEDEF ────────────────────────────────────────────────────────────

        /// <summary>Günlük ciro hedefi. İleride Settings tablosundan okunabilir.</summary>
        public decimal DailyRevenueTarget { get; set; } = 10_000m;

        /// <summary>
        /// Hedefe ulaşma yüzdesi — progress bar ve SVG ring için.
        /// 0–100 aralığına kırpılır; fazla mesai günleri %100 kalır.
        /// </summary>
        public decimal RevenueTargetPercentage =>
            DailyRevenueTarget > 0
                ? Math.Min(Math.Round(DailyTotalRevenue / DailyRevenueTarget * 100, 1), 100)
                : 0;

        // ── MASA DURUMU ──────────────────────────────────────────────────────

        /// <summary>Masa sayı özeti (Boş / Dolu / Rezerve).</summary>
        public TableStatusSummaryData TableStatus { get; set; } = new();

        /// <summary>Masa ısı haritası için tam masa listesi.</summary>
        public List<TableHeatmapItem> TableHeatmap { get; set; } = new();

        // ── GRAFİK VERİLERİ ──────────────────────────────────────────────────

        /// <summary>Saatlik sipariş yoğunluğu — AreaChart (08:00–23:00).</summary>
        public List<HourlyTrendPoint> HourlyOrderTrends { get; set; } = new();

        /// <summary>Bugünkü en çok satan 5 ürün — Doughnut Chart.</summary>
        public List<TopProductPoint> TopSellingProducts { get; set; } = new();

        /// <summary>Haftalık ciro karşılaştırması — LineChart (Bu hafta vs Geçen hafta).</summary>
        public List<WeeklyTrendPoint> WeeklyTrend { get; set; } = new();

        // ── STOK UYARILARI ────────────────────────────────────────────────────

        /// <summary>Düşük/kritik stok uyarısı olan ürünler (max 5 kart gösterilir).</summary>
        public List<StockAlertItem> StockAlerts { get; set; } = new();

        // ── HIZLI NOTLAR ─────────────────────────────────────────────────────

        /// <summary>IsWaiterCalled == true olan masa sayısı.</summary>
        public int WaiterCallsActive { get; set; }

        /// <summary>StockQuantity ≤ AlertThreshold olan TrackStock=true ürün sayısı.</summary>
        public int LowStockAlerts { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  YARDIMCI SINIFLAR
    // ─────────────────────────────────────────────────────────────────────────

    public class TableStatusSummaryData
    {
        public int Empty { get; set; }   // TableStatus == 0
        public int Occupied { get; set; }   // TableStatus == 1
        public int Reserved { get; set; }   // TableStatus == 2

        public int Total => Empty + Occupied + Reserved;
        public decimal OccupancyRate =>
            Total > 0 ? Math.Round((decimal)Occupied / Total * 100, 1) : 0;
    }

    public class TableHeatmapItem
    {
        public int TableId { get; set; }
        public string TableName { get; set; } = string.Empty;

        /// <summary>"empty" | "occupied" | "reserved" | "waiter"</summary>
        public string Status { get; set; } = "empty";

        /// <summary>Aktif adisyonun anlık toplam tutarı (₺).</summary>
        public decimal CurrentBill { get; set; }

        /// <summary>
        /// Masadaki kişi tahmini.
        /// Rezerve → ReservationGuestCount | Dolu → TableCapacity
        /// </summary>
        public int GuestCount { get; set; }

        /// <summary>"28dk" | "1s 14dk" formatında adisyon süresi.</summary>
        public string DurationText { get; set; } = string.Empty;

        /// <summary>Rezerve masalar için "HH:mm" formatında beklenen saat.</summary>
        public string? ReservationTime { get; set; }
    }

    public class HourlyTrendPoint
    {
        public string HourLabel { get; set; } = string.Empty; // "09:00"
        public int OrderCount { get; set; }
        public decimal Revenue { get; set; }
    }

    public class TopProductPoint
    {
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal Revenue { get; set; }
    }

    public class WeeklyTrendPoint
    {
        public string DayLabel { get; set; } = string.Empty; // "Pzt", "Sal" …
        public decimal ThisWeek { get; set; }
        public decimal LastWeek { get; set; }
    }

    public class StockAlertItem
    {
        public string ProductName { get; set; } = string.Empty;
        public decimal CurrentQty { get; set; }
        public decimal AlertThreshold { get; set; }
        public string Unit { get; set; } = "ad";

        /// <summary>
        /// Progress bar üst sınırı: AlertThreshold * 3 (eşiğin 3 katı = "tam dolu").
        /// AlertThreshold == 0 ise CurrentQty * 2 kullanılır.
        /// </summary>
        public decimal MaxQty =>
            AlertThreshold > 0 ? AlertThreshold * 3 : Math.Max(CurrentQty * 2, 1);

        /// <summary>"critical" | "low" | "ok"</summary>
        public string Level { get; set; } = "ok";

        public decimal PercentRemaining =>
            MaxQty > 0 ? Math.Round(CurrentQty / MaxQty * 100, 1) : 0;
    }
}
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Dtos.Shift;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared.Common;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Shift;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Controllers
{
    [Authorize(Roles = "Admin")]
    public class ShiftController : Controller
    {
        private readonly RestaurantDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<RestaurantHub> _hub;
        private readonly ITenantService _tenantService; // [MT]

        public ShiftController(
            RestaurantDbContext db,
            UserManager<ApplicationUser> userManager,
            IHubContext<RestaurantHub> hub,
            ITenantService tenantService)  // [MT]
        {
            _db = db;
            _userManager = userManager;
            _hub = hub;
            _tenantService = tenantService; // [MT]
        }

        // ── GET /Shift ────────────────────────────────────────────────────────
        public async Task<IActionResult> Index(int page = 1)
        {
            const int pageSize = 20;

            var activeShift = await _db.ShiftLogs
                .Include(s => s.OpenedByUser)
                .FirstOrDefaultAsync(s => !s.IsClosed);

            var query = _db.ShiftLogs
                .Include(s => s.OpenedByUser)
                .Include(s => s.ClosedByUser)
                .OrderByDescending(s => s.OpenedAt);

            var totalCount = await query.CountAsync();
            var shifts = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.ActiveShift = activeShift;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.TotalCount = totalCount;

            return View(shifts);
        }

        // ── GET /Shift/Open ───────────────────────────────────────────────────
        public async Task<IActionResult> Open()
        {
            var hasOpen = await _db.ShiftLogs.AnyAsync(s => !s.IsClosed);
            if (hasOpen)
            {
                TempData["Warning"] = "Zaten açık bir vardiya mevcut. Önce kapatın.";
                return RedirectToAction(nameof(Index));
            }
            return View();
        }

        // ── POST /Shift/Open ──────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> Open([FromBody] ShiftOpenDto dto)
        {
            var hasOpen = await _db.ShiftLogs.AnyAsync(s => !s.IsClosed);
            if (hasOpen)
                return Conflict(new { success = false, message = "Zaten açık bir vardiya var." });

            var user = await _userManager.GetUserAsync(User);

            var shift = new ShiftLog
            {
                OpenedAt = DateTime.UtcNow,
                OpenedByUserId = user!.Id,
                OpeningBalance = dto.OpeningBalance,
                DifferenceThreshold = dto.DifferenceThreshold > 0 ? dto.DifferenceThreshold : 100m,
                Notes = dto.Notes,
                IsClosed = false,
                IsLocked = false,
                TenantId = _tenantService.TenantId!  // [MT] izolasyon anahtarı
            };

            _db.ShiftLogs.Add(shift);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, shiftId = shift.ShiftLogId });
        }

        // ── GET /Shift/Detail/{id} ────────────────────────────────────────────
        public async Task<IActionResult> Detail(int id)
        {
            var shift = await _db.ShiftLogs
                .Include(s => s.OpenedByUser)
                .Include(s => s.ClosedByUser)
                .FirstOrDefaultAsync(s => s.ShiftLogId == id);

            if (shift == null) return NotFound();

            var vm = await BuildDetailViewModel(shift);
            return View(vm);
        }

        // ── GET /Shift/Close/{id} ─────────────────────────────────────────────
        public async Task<IActionResult> Close(int id)
        {
            var shift = await _db.ShiftLogs
                .Include(s => s.OpenedByUser)
                .FirstOrDefaultAsync(s => s.ShiftLogId == id && !s.IsClosed);

            if (shift == null)
            {
                TempData["Error"] = "Vardiya bulunamadı veya zaten kapatılmış.";
                return RedirectToAction(nameof(Index));
            }

            return View(shift);
        }

        // ── POST /Shift/Close/{id} ────────────────────────────────────────────
        // Gövde: { closingBalance, notes }  — threshold opsiyonel
        [HttpPost]
        public async Task<IActionResult> Close(int id, [FromBody] ShiftCloseDto dto)
        {
            var shift = await _db.ShiftLogs
                .FirstOrDefaultAsync(s => s.ShiftLogId == id && !s.IsClosed);

            if (shift == null)
                return NotFound(new { success = false, message = "Vardiya bulunamadı veya zaten kapalı." });

            // ── [SPRINT-1] Vardiya Kapatma Kilidi ────────────────────────────
            // Bu vardiya süresince açılmış ve hâlâ Open durumunda olan sipariş
            // var mı? Varsa kapatmaya izin verme.
            var hasOpenOrders = await _db.Orders
                .AnyAsync(o => o.OrderStatus == OrderStatus.Open
                            && o.OrderOpenedAt >= shift.OpenedAt);

            if (hasOpenOrders)
                return BadRequest(new
                {
                    success = false,
                    message = "Açık masalar varken vardiya kapatılamaz. Lütfen önce tüm hesapları kapatın."
                });
            // ─────────────────────────────────────────────────────────────────

            var user = await _userManager.GetUserAsync(User);
            var closedAt = DateTime.UtcNow;

            // ── Sistem hesaplamaları ──────────────────────────────────────────
            // Vardiya aralığındaki (OpenedAt..closedAt) paid Order'ları al
            var paidOrders = await _db.Orders
                .Include(o => o.Payments)
                .Where(o => o.OrderStatus == OrderStatus.Paid // FIX: Enum olarak değiştirildi
                         && o.OrderClosedAt >= shift.OpenedAt
                         && o.OrderClosedAt <= closedAt)
                .ToListAsync();

            var allPayments = paidOrders.SelectMany(o => o.Payments).ToList();

            decimal totalSales = paidOrders.Sum(o => o.OrderTotalAmount);
            decimal totalCash = allPayments.Where(p => p.PaymentsMethod == 0).Sum(p => p.PaymentsAmount);
            decimal totalCard = allPayments.Where(p => p.PaymentsMethod == 1 || p.PaymentsMethod == 2).Sum(p => p.PaymentsAmount);
            decimal totalOther = allPayments.Where(p => p.PaymentsMethod == 3).Sum(p => p.PaymentsAmount);

            // İndirim: (SiparişToplamı - ÖdenenToplam) farkı — negatif olamaz
            decimal paidTotal = allPayments.Sum(p => p.PaymentsAmount);
            decimal totalDiscount = Math.Max(0, totalSales - paidTotal);

            // ── [v4 — StockLog Mimarisi] Zayi & Stok İade ────────────────────────
            // SORUN (v1-v3): OrderItem.IsWasted tek satır; aynı kalem önce zayi
            //   sonra stok iadesi yapılırsa IsWasted=false eziliyor → zayi tutarı ₺0.
            //
            // ÇÖZÜM: StockLog her iptal işlemini ayrı satırda tutar:
            //   • Zayi     → SourceType="SiparişKaynaklı", MovementType="Çıkış"
            //   • Stokİade → SourceType=null, Note LIKE "İptal iadesi%", MovementType="Giriş"
            //   Zaman filtresi: StockLog.CreatedAt (işlem anı) → vardiya aralığına göre.

            // 1) Zayi (gerçek finansal kayıp)
            var wasteStockLogs = await _db.StockLogs
                .Include(sl => sl.MenuItem)
                .Where(sl => sl.SourceType == "SiparişKaynaklı"
                          && sl.CreatedAt >= shift.OpenedAt
                          && sl.CreatedAt <= closedAt)
                .ToListAsync();

            int totalWasteCount = wasteStockLogs.Sum(sl => Math.Abs(sl.QuantityChange));
            decimal totalWaste = wasteStockLogs.Sum(sl =>
            {
                decimal price = sl.UnitPrice ?? sl.MenuItem?.MenuItemPrice ?? 0m;
                return Math.Abs(sl.QuantityChange) * price;
            });

            // 2) Stok İade (mali kayıp yok — sadece adeti rapor et)
            int totalStockReturnCount = await _db.StockLogs
                .Where(sl => sl.SourceType == null
                          && sl.MovementType == "Giriş"
                          && sl.Note != null && sl.Note.StartsWith("İptal iadesi")
                          && sl.CreatedAt >= shift.OpenedAt
                          && sl.CreatedAt <= closedAt)
                .SumAsync(sl => (int?)sl.QuantityChange) ?? 0;

            // Kasa farkı: ClosingBalance - (OpeningBalance + TotalCash)
            decimal difference = dto.ClosingBalance - (shift.OpeningBalance + totalCash);

            // ── ShiftLog'u güncelle ───────────────────────────────────────────
            shift.ClosedAt = closedAt;
            shift.ClosedByUserId = user!.Id;
            shift.ClosingBalance = dto.ClosingBalance;
            shift.Notes = dto.Notes ?? shift.Notes;
            if (dto.DifferenceThreshold > 0)
                shift.DifferenceThreshold = dto.DifferenceThreshold;

            shift.TotalSales = totalSales;
            shift.TotalCash = totalCash;
            shift.TotalCard = totalCard;
            shift.TotalOther = totalOther;
            shift.TotalDiscount = totalDiscount;
            shift.TotalWaste = totalWaste;
            shift.Difference = difference;
            shift.IsClosed = true;

            await _db.SaveChangesAsync();

            // ── SignalR: fark eşiği aşıldıysa uyarı gönder ───────────────────
            if (Math.Abs(difference) > shift.DifferenceThreshold)
            {
                await _hub.Clients.Group(_tenantService.TenantId ?? "").SendAsync("ShiftDifferenceAlert", new // NEW EKLENDİ
                {
                    shiftId = shift.ShiftLogId,
                    difference = shift.Difference,
                    threshold = shift.DifferenceThreshold,
                    closedBy = user.FullName ?? user.UserName
                });
            }

            return Ok(new { success = true, shiftId = shift.ShiftLogId });
        }

        // ── POST /Shift/ToggleLock/{id} ───────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> ToggleLock(int id)
        {
            var shift = await _db.ShiftLogs.FindAsync(id);
            if (shift == null) return NotFound(new { success = false });

            shift.IsLocked = !shift.IsLocked;
            await _db.SaveChangesAsync();

            return Ok(new { success = true, isLocked = shift.IsLocked });
        }

        // ── GET /Shift/PreviewTotals/{id} — Close sayfası AJAX önizleme ───────
        [HttpGet]
        public async Task<IActionResult> PreviewTotals(int id)
        {
            var shift = await _db.ShiftLogs
                .FirstOrDefaultAsync(s => s.ShiftLogId == id && !s.IsClosed);
            if (shift == null) return NotFound();

            var now = DateTime.UtcNow;

            var paidOrders = await _db.Orders
                .Include(o => o.Payments)
                .Where(o => o.OrderStatus == OrderStatus.Paid // FIX: Enum olarak değiştirildi
                         && o.OrderClosedAt >= shift.OpenedAt
                         && o.OrderClosedAt <= now)
                .ToListAsync();

            var allPayments = paidOrders.SelectMany(o => o.Payments).ToList();

            decimal totalSales = paidOrders.Sum(o => o.OrderTotalAmount);
            decimal totalCash = allPayments.Where(p => p.PaymentsMethod == 0).Sum(p => p.PaymentsAmount);
            decimal totalCard = allPayments.Where(p => p.PaymentsMethod == 1 || p.PaymentsMethod == 2).Sum(p => p.PaymentsAmount);
            decimal totalOther = allPayments.Where(p => p.PaymentsMethod == 3).Sum(p => p.PaymentsAmount);
            decimal paidTotal = allPayments.Sum(p => p.PaymentsAmount);
            decimal totalDiscount = Math.Max(0, totalSales - paidTotal);

            // ── [v4 — StockLog Mimarisi] Zayi & Stok İade (PreviewTotals) ─────────
            var wasteStockLogs = await _db.StockLogs
                .Include(sl => sl.MenuItem)
                .Where(sl => sl.SourceType == "SiparişKaynaklı"
                          && sl.CreatedAt >= shift.OpenedAt
                          && sl.CreatedAt <= now)
                .ToListAsync();

            int totalWasteCount = wasteStockLogs.Sum(sl => Math.Abs(sl.QuantityChange));
            decimal totalWaste = wasteStockLogs.Sum(sl =>
            {
                decimal price = sl.UnitPrice ?? sl.MenuItem?.MenuItemPrice ?? 0m;
                return Math.Abs(sl.QuantityChange) * price;
            });

            int totalStockReturnCount = await _db.StockLogs
                .Where(sl => sl.SourceType == null
                          && sl.MovementType == "Giriş"
                          && sl.Note != null && sl.Note.StartsWith("İptal iadesi")
                          && sl.CreatedAt >= shift.OpenedAt
                          && sl.CreatedAt <= now)
                .SumAsync(sl => (int?)sl.QuantityChange) ?? 0;

            return Ok(new
            {
                totalSales,
                totalCash,
                totalCard,
                totalOther,
                totalDiscount,
                totalWaste,
                totalWasteCount,
                totalStockReturnCount,
                openingBalance = shift.OpeningBalance,
                differenceThreshold = shift.DifferenceThreshold
            });
        }

        // ── GET /Shift/GeneratePdf/{id} ───────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GeneratePdf(int id)
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var shift = await _db.ShiftLogs
                .Include(s => s.OpenedByUser)
                .Include(s => s.ClosedByUser)
                .FirstOrDefaultAsync(s => s.ShiftLogId == id);

            if (shift == null) return NotFound();

            var vm = await BuildDetailViewModel(shift);
            var s = shift;
            var duration = (s.ClosedAt ?? DateTime.UtcNow) - s.OpenedAt;
            bool over = s.IsClosed && Math.Abs(s.Difference) > s.DifferenceThreshold;
            var reportDate = DateTime.Now;
            decimal totalPmt = vm.TotalCash + vm.TotalCreditCard + vm.TotalDebitCard + vm.TotalOther;

            var rowEven = "#f8fafc";
            var rowOdd = "#ffffff";
            var okClr = "#22c55e";
            var warnClr = "#ef4444";
            var borderClr = "#e2e8f0";
            var mutedClr = "#64748b";
            var accentClr = "#f97316";
            var diffColor = over ? warnClr : okClr;

            var pdfBytes = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(28);
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                    page.Header().Column(col =>
                    {
                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text($"Z-Raporu — Vardiya #{s.ShiftLogId}")
                                    .FontSize(18).Bold().FontColor("#1e293b");
                                c.Item().Text($"Açılış: {s.OpenedAt.ToLocalTime():dd.MM.yyyy HH:mm}  →  Kapanış: {(s.ClosedAt.HasValue ? s.ClosedAt.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "Açık")}  ·  Süre: {(int)duration.TotalHours}s {duration.Minutes}dk")
                                    .FontSize(9).FontColor(mutedClr);
                            });
                            row.ConstantItem(160).AlignRight().Column(c =>
                            {
                                c.Item().Text($"Açan: {s.OpenedByUser?.FullName ?? s.OpenedByUser?.UserName ?? "—"}")
                                    .FontSize(9).FontColor(mutedClr).AlignRight();
                                c.Item().Text($"Kapatan: {s.ClosedByUser?.FullName ?? s.ClosedByUser?.UserName ?? "—"}")
                                    .FontSize(9).FontColor(mutedClr).AlignRight();
                                c.Item().Text($"Oluşturulma: {reportDate:dd.MM.yyyy HH:mm}")
                                    .FontSize(8).FontColor(mutedClr).AlignRight();
                            });
                        });
                        col.Item().PaddingTop(6).LineHorizontal(1.5f).LineColor("#1e293b");
                        col.Item().PaddingBottom(6);
                    });

                    page.Footer().AlignCenter().Text(text =>
                    {
                        text.Span("Sayfa ").FontSize(8).FontColor(mutedClr);
                        text.CurrentPageNumber().FontSize(8).FontColor(mutedClr);
                        text.Span(" / ").FontSize(8).FontColor(mutedClr);
                        text.TotalPages().FontSize(8).FontColor(mutedClr);
                        text.Span($"   •   Z-Raporu #{s.ShiftLogId}   •   {reportDate:dd.MM.yyyy HH:mm}")
                            .FontSize(8).FontColor(mutedClr);
                    });

                    page.Content().Column(col =>
                    {
                        // §1 KPI
                        col.Item().PaddingBottom(8).Row(row =>
                        {
                            static IContainer KpiBox(IContainer c) =>
                                c.Border(0.5f).BorderColor("#e2e8f0").Padding(10).AlignCenter();

                            row.RelativeItem().Element(KpiBox).Column(c =>
                            {
                                c.Item().Text("TOPLAM SATIŞ").FontSize(7).FontColor(mutedClr).Bold();
                                c.Item().Text($"₺{s.TotalSales:N2}").FontSize(14).Bold().FontColor(accentClr);
                            });
                            row.RelativeItem().Element(KpiBox).Column(c =>
                            {
                                c.Item().Text("NAKİT").FontSize(7).FontColor(mutedClr).Bold();
                                c.Item().Text($"₺{s.TotalCash:N2}").FontSize(14).Bold().FontColor("#10b981");
                            });
                            row.RelativeItem().Element(KpiBox).Column(c =>
                            {
                                c.Item().Text("KART").FontSize(7).FontColor(mutedClr).Bold();
                                c.Item().Text($"₺{s.TotalCard:N2}").FontSize(14).Bold().FontColor("#3b82f6");
                            });
                            row.RelativeItem().Element(KpiBox).Column(c =>
                            {
                                c.Item().Text("KASA FARKI").FontSize(7).FontColor(mutedClr).Bold();
                                c.Item().Text($"{(s.Difference >= 0 ? "+" : "")}₺{s.Difference:N2}")
                                    .FontSize(14).Bold().FontColor(diffColor);
                            });
                        });

                        // §2 Kasa Sayımı
                        col.Item().PaddingBottom(4).Text("§ Kasa Sayımı").FontSize(10).Bold().FontColor("#1e293b");
                        col.Item().PaddingBottom(8).Table(table =>
                        {
                            table.ColumnsDefinition(cols => { cols.RelativeColumn(3); cols.RelativeColumn(2); });
                            static IContainer H(IContainer c) => c.Background("#1e293b").Padding(5).AlignMiddle();
                            table.Header(h => {
                                h.Cell().Element(H).Text("Kalem").Bold().FontColor(Colors.White).FontSize(8);
                                h.Cell().Element(H).Text("Tutar").Bold().FontColor(Colors.White).FontSize(8).AlignRight();
                            });
                            var cashRows = new[] {
                                ("Açılış Bakiyesi",  $"₺{s.OpeningBalance:N2}",  mutedClr),
                                ("Kapanış Bakiyesi", $"₺{s.ClosingBalance:N2}",  mutedClr),
                                ("Kasa Farkı",       $"{(s.Difference>=0?"+":"")}₺{s.Difference:N2}", diffColor),
                            };
                            int ri = 0;
                            foreach (var (lbl, val, clr) in cashRows)
                            {
                                var bg = ri++ % 2 == 0 ? rowEven : rowOdd;
                                IContainer D(IContainer c) => c.Background(bg).BorderBottom(0.5f).BorderColor(borderClr).Padding(5).AlignMiddle();
                                table.Cell().Element(D).Text(lbl).FontSize(9).Bold();
                                table.Cell().Element(D).Text(val).FontSize(9).Bold().FontColor(clr).AlignRight();
                            }
                        });
                        if (over)
                        {
                            col.Item().PaddingBottom(8)
                                .Background("#fef2f2").Border(0.5f).BorderColor("#fecaca").Padding(8)
                                .Text($"UYARI: Kasa farkı eşiği (₺{s.DifferenceThreshold:N0}) aşıldı!")
                                .FontSize(9).Bold().FontColor(warnClr);
                        }

                        // §3 Ödeme Dağılımı
                        col.Item().PaddingBottom(4).Text("§ Ödeme Dağılımı").FontSize(10).Bold().FontColor("#1e293b");
                        col.Item().PaddingBottom(8).Table(table =>
                        {
                            table.ColumnsDefinition(cols => {
                                cols.RelativeColumn(3);
                                cols.RelativeColumn(2);
                                cols.RelativeColumn(1.5f);
                            });
                            static IContainer H(IContainer c) => c.Background("#1e293b").Padding(5).AlignMiddle();
                            table.Header(h => {
                                h.Cell().Element(H).Text("Yöntem").Bold().FontColor(Colors.White).FontSize(8);
                                h.Cell().Element(H).Text("Tutar").Bold().FontColor(Colors.White).FontSize(8).AlignRight();
                                h.Cell().Element(H).Text("%").Bold().FontColor(Colors.White).FontSize(8).AlignRight();
                            });
                            var pmtRows = new[] {
                                ("Nakit",       vm.TotalCash),
                                ("Kredi Kartı", vm.TotalCreditCard),
                                ("Banka Kartı", vm.TotalDebitCard),
                                ("Diğer",       vm.TotalOther),
                            };
                            int ri = 0;
                            foreach (var (lbl, amt) in pmtRows)
                            {
                                var bg = ri++ % 2 == 0 ? rowEven : rowOdd;
                                var pct = totalPmt > 0 ? (double)(amt / totalPmt * 100) : 0;
                                IContainer D(IContainer c) => c.Background(bg).BorderBottom(0.5f).BorderColor(borderClr).Padding(5).AlignMiddle();
                                table.Cell().Element(D).Text(lbl).FontSize(9);
                                table.Cell().Element(D).Text($"₺{amt:N2}").FontSize(9).AlignRight();
                                table.Cell().Element(D).Text($"{pct:F1}%").FontSize(9).FontColor(mutedClr).AlignRight();
                            }
                        });

                        // §4 İptal & Zayi
                        col.Item().PaddingBottom(4).Text("§ İptal & Zayi").FontSize(10).Bold().FontColor("#1e293b");
                        col.Item().PaddingBottom(8).Row(row =>
                        {
                            row.RelativeItem().Border(0.5f).BorderColor(borderClr).Padding(8).AlignCenter().Column(c =>
                            {
                                c.Item().Text("İptal Edilen Kalem").FontSize(7).FontColor(mutedClr).Bold();
                                c.Item().Text($"{vm.WasteCount} zayi / {vm.StockReturnCount} iade").FontSize(12).Bold().FontColor("#f59e0b");
                            });
                            row.ConstantItem(16);
                            row.RelativeItem().Border(0.5f).BorderColor(borderClr).Padding(8).AlignCenter().Column(c =>
                            {
                                c.Item().Text("Zayi Tutarı").FontSize(7).FontColor(mutedClr).Bold();
                                c.Item().Text($"₺{vm.WasteAmount:N2}").FontSize(14).Bold().FontColor(warnClr);
                            });
                            row.ConstantItem(16);
                            row.RelativeItem().Border(0.5f).BorderColor(borderClr).Padding(8).AlignCenter().Column(c =>
                            {
                                c.Item().Text("Toplam İndirim").FontSize(7).FontColor(mutedClr).Bold();
                                c.Item().Text($"₺{s.TotalDiscount:N2}").FontSize(14).Bold().FontColor("#3b82f6");
                            });
                        });

                        // §5 Top 5 Ürün
                        if (vm.TopProducts.Any())
                        {
                            col.Item().PaddingBottom(4).Text("§ En Çok Satan Top 5").FontSize(10).Bold().FontColor("#1e293b");
                            col.Item().PaddingBottom(8).Table(table =>
                            {
                                table.ColumnsDefinition(cols => {
                                    cols.ConstantColumn(22);
                                    cols.RelativeColumn(4);
                                    cols.RelativeColumn(1.5f);
                                    cols.RelativeColumn(2);
                                });
                                static IContainer H(IContainer c) => c.Background("#1e293b").Padding(5).AlignMiddle();
                                table.Header(h => {
                                    h.Cell().Element(H).Text("#").Bold().FontColor(Colors.White).FontSize(8);
                                    h.Cell().Element(H).Text("Ürün").Bold().FontColor(Colors.White).FontSize(8);
                                    h.Cell().Element(H).Text("Adet").Bold().FontColor(Colors.White).FontSize(8).AlignRight();
                                    h.Cell().Element(H).Text("Tutar").Bold().FontColor(Colors.White).FontSize(8).AlignRight();
                                });
                                for (int i = 0; i < vm.TopProducts.Count; i++)
                                {
                                    var p = vm.TopProducts[i];
                                    var bg = i % 2 == 0 ? rowEven : rowOdd;
                                    IContainer D(IContainer c) => c.Background(bg).BorderBottom(0.5f).BorderColor(borderClr).Padding(5).AlignMiddle();
                                    table.Cell().Element(D).Text((i + 1).ToString()).FontSize(9).Bold().FontColor(accentClr);
                                    table.Cell().Element(D).Text(p.ProductName).FontSize(9);
                                    table.Cell().Element(D).Text(p.Quantity.ToString()).FontSize(9).AlignRight();
                                    table.Cell().Element(D).Text($"₺{p.TotalAmount:N2}").FontSize(9).AlignRight();
                                }
                            });
                        }

                        // §6 Kategori
                        if (vm.CategorySales.Any())
                        {
                            col.Item().PaddingBottom(4).Text("§ Kategori Dağılımı").FontSize(10).Bold().FontColor("#1e293b");
                            col.Item().PaddingBottom(8).Table(table =>
                            {
                                table.ColumnsDefinition(cols => { cols.RelativeColumn(4); cols.RelativeColumn(2); });
                                static IContainer H(IContainer c) => c.Background("#1e293b").Padding(5).AlignMiddle();
                                table.Header(h => {
                                    h.Cell().Element(H).Text("Kategori").Bold().FontColor(Colors.White).FontSize(8);
                                    h.Cell().Element(H).Text("Tutar").Bold().FontColor(Colors.White).FontSize(8).AlignRight();
                                });
                                for (int i = 0; i < vm.CategorySales.Count; i++)
                                {
                                    var cat = vm.CategorySales[i];
                                    var bg = i % 2 == 0 ? rowEven : rowOdd;
                                    IContainer D(IContainer cc) => cc.Background(bg).BorderBottom(0.5f).BorderColor(borderClr).Padding(5).AlignMiddle();
                                    table.Cell().Element(D).Text(cat.CategoryName).FontSize(9);
                                    table.Cell().Element(D).Text($"₺{cat.TotalAmount:N2}").FontSize(9).AlignRight();
                                }
                            });
                        }

                        // §7 Garson
                        if (vm.WaiterSales.Any())
                        {
                            col.Item().PaddingBottom(4).Text("§ Garson Bazında Satış").FontSize(10).Bold().FontColor("#1e293b");
                            col.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols => {
                                    cols.RelativeColumn(3);
                                    cols.RelativeColumn(1.5f);
                                    cols.RelativeColumn(2);
                                    cols.RelativeColumn(2);
                                });
                                static IContainer H(IContainer c) => c.Background("#1e293b").Padding(5).AlignMiddle();
                                table.Header(h => {
                                    h.Cell().Element(H).Text("Garson").Bold().FontColor(Colors.White).FontSize(8);
                                    h.Cell().Element(H).Text("Sipariş").Bold().FontColor(Colors.White).FontSize(8).AlignRight();
                                    h.Cell().Element(H).Text("Toplam").Bold().FontColor(Colors.White).FontSize(8).AlignRight();
                                    h.Cell().Element(H).Text("Ort. Sepet").Bold().FontColor(Colors.White).FontSize(8).AlignRight();
                                });
                                for (int i = 0; i < vm.WaiterSales.Count; i++)
                                {
                                    var w = vm.WaiterSales[i];
                                    var bg = i % 2 == 0 ? rowEven : rowOdd;
                                    IContainer D(IContainer c) => c.Background(bg).BorderBottom(0.5f).BorderColor(borderClr).Padding(5).AlignMiddle();
                                    table.Cell().Element(D).Text(w.WaiterName).FontSize(9);
                                    table.Cell().Element(D).Text(w.OrderCount.ToString()).FontSize(9).AlignRight();
                                    table.Cell().Element(D).Text($"₺{w.TotalAmount:N2}").FontSize(9).AlignRight();
                                    table.Cell().Element(D).Text($"₺{w.AverageBasket:N2}").FontSize(9).AlignRight();
                                }
                            });
                        }
                    });
                });
            }).GeneratePdf();

            return File(pdfBytes, "application/pdf",
                $"z_raporu_vardiya_{s.ShiftLogId}_{reportDate:yyyyMMdd_HHmm}.pdf");
        }

        // ═════════════════════════════════════════════════════════════════════
        // PRIVATE — BuildDetailViewModel
        // ═════════════════════════════════════════════════════════════════════
        private async Task<ShiftDetailViewModel> BuildDetailViewModel(ShiftLog shift)
        {
            var closedAt = shift.ClosedAt ?? DateTime.UtcNow;

            // Vardiya aralığındaki ödenmiş siparişler
            var paidOrders = await _db.Orders
                .Include(o => o.Payments)
                .Include(o => o.OrderItems)
                    .ThenInclude(oi => oi.MenuItem)
                        .ThenInclude(mi => mi.Category)
                .Where(o => o.OrderStatus == OrderStatus.Paid // FIX: Enum olarak değiştirildi
                         && o.OrderClosedAt >= shift.OpenedAt
                         && o.OrderClosedAt <= closedAt)
                .ToListAsync();

            var allPayments = paidOrders.SelectMany(o => o.Payments).ToList();

            decimal cash = allPayments.Where(p => p.PaymentsMethod == 0).Sum(p => p.PaymentsAmount);
            decimal creditCard = allPayments.Where(p => p.PaymentsMethod == 1).Sum(p => p.PaymentsAmount);
            decimal debitCard = allPayments.Where(p => p.PaymentsMethod == 2).Sum(p => p.PaymentsAmount);
            decimal other = allPayments.Where(p => p.PaymentsMethod == 3).Sum(p => p.PaymentsAmount);

            // ── [v4 — StockLog Mimarisi] BuildDetailViewModel Zayi & Stok İade ────
            // OrderItem.IsWasted ezilme problemi: aynı kalem hem zayi hem iade edilince
            // IsWasted son işleme göre eziliyor → zayi tutarı ₺0 görünüyor.
            // ÇÖZÜM: Her iptal işlemi StockLog'a ayrı satır olarak yazılıyor:
            //   • Zayi     → SourceType="SiparişKaynaklı", MovementType="Çıkış"
            //   • Stok İade→ SourceType=null, Note LIKE "İptal iadesi%", MovementType="Giriş"

            var wasteStockLogs = await _db.StockLogs
                .Include(sl => sl.MenuItem)
                .Where(sl => sl.SourceType == "SiparişKaynaklı"
                          && sl.CreatedAt >= shift.OpenedAt
                          && sl.CreatedAt <= closedAt)
                .ToListAsync();

            int wasteCount = wasteStockLogs.Sum(sl => Math.Abs(sl.QuantityChange));
            decimal wasteAmount = wasteStockLogs.Sum(sl =>
            {
                decimal price = sl.UnitPrice ?? sl.MenuItem?.MenuItemPrice ?? 0m;
                return Math.Abs(sl.QuantityChange) * price;
            });

            int stockReturnCount = await _db.StockLogs
                .Where(sl => sl.SourceType == null
                          && sl.MovementType == "Giriş"
                          && sl.Note != null && sl.Note.StartsWith("İptal iadesi")
                          && sl.CreatedAt >= shift.OpenedAt
                          && sl.CreatedAt <= closedAt)
                .SumAsync(sl => (int?)sl.QuantityChange) ?? 0;

            // Garson bazlı satış
            var waiterSales = paidOrders
                .GroupBy(o => o.OrderOpenedBy)
                .Select(g => new WaiterSalesRow
                {
                    WaiterName = g.Key ?? "Bilinmiyor",
                    OrderCount = g.Count(),
                    TotalAmount = g.Sum(o => o.OrderTotalAmount)
                })
                .OrderByDescending(w => w.TotalAmount)
                .ToList();

            // Top 5 ürün — FIX: ActiveQuantity veya PaidQuantity, tutar OrderItemLineTotal
            var topProducts = paidOrders
                .SelectMany(o => o.OrderItems)
                .Where(oi => oi.ActiveQuantity > 0)
                .GroupBy(oi => oi.MenuItem?.MenuItemName ?? "Bilinmiyor")
                .Select(g => new TopProductRow
                {
                    ProductName = g.Key,
                    Quantity = g.Sum(oi => oi.ActiveQuantity),
                    TotalAmount = g.Sum(oi => oi.OrderItemLineTotal)
                })
                .OrderByDescending(p => p.TotalAmount)
                .Take(5)
                .ToList();

            // Kategori dağılımı
            var categorySales = paidOrders
                .SelectMany(o => o.OrderItems)
                .Where(oi => oi.ActiveQuantity > 0)
                .GroupBy(oi => oi.MenuItem?.Category?.CategoryName ?? "Diğer")
                .Select(g => new CategorySalesRow
                {
                    CategoryName = g.Key,
                    TotalAmount = g.Sum(oi => oi.OrderItemLineTotal)
                })
                .OrderByDescending(c => c.TotalAmount)
                .ToList();

            return new ShiftDetailViewModel
            {
                ShiftLog = shift,
                TotalCash = cash,
                TotalCreditCard = creditCard,
                TotalDebitCard = debitCard,
                TotalOther = other,
                WasteCount = wasteCount,          // zayi adedi   (StockLog kaynaklı)
                WasteAmount = wasteAmount,          // zayi tutarı  (StockLog kaynaklı)
                StockReturnCount = stockReturnCount,     // stok iade adedi (mali kayıp yok)
                WaiterSales = waiterSales,
                TopProducts = topProducts,
                CategorySales = categorySales
            };
        }
    }
}
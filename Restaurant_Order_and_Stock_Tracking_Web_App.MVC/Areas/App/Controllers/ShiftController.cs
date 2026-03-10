// ════════════════════════════════════════════════════════════════════════════
//  Areas/App/Controllers/ShiftController.cs
//  Yol: Areas/App/Controllers/ShiftController.cs
//
//  SPRINT 5 — Areas Refactoring:
//  [S5-NS]   Namespace → ...Areas.App.Controllers
//  [S5-BASE] Controller → AppBaseController ([Area("App")] + AppAuth miras alındı)
//  [S5-URL]  Tüm RedirectToAction çağrıları aynı controller içi (nameof(Index))
//            → area parametresi gerekmez. Bütünlük için explicit eklendi.
//
//  [RC-03] Çift Vardiya Kapatma Race Condition → RepeatableRead TX
//  [P-02]  Tüm parasal toplamlar DB SumAsync ile hesaplanır (InMemory yok)
//  [F-01]  İndirim = GrossSatış − TümÖdemeler (DB aggregate farkından)
// ════════════════════════════════════════════════════════════════════════════

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

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.App.Controllers
{
    [Authorize(Roles = "Admin")]
    public class ShiftController : AppBaseController
    {
        private readonly RestaurantDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<RestaurantHub> _hub;
        private readonly ITenantService _tenantService;

        public ShiftController(
            RestaurantDbContext db,
            UserManager<ApplicationUser> userManager,
            IHubContext<RestaurantHub> hub,
            ITenantService tenantService)
        {
            _db = db;
            _userManager = userManager;
            _hub = hub;
            _tenantService = tenantService;
        }

        // ── GET /App/Shift ────────────────────────────────────────────────────
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

        // ── GET /App/Shift/Open ───────────────────────────────────────────────
        public async Task<IActionResult> Open()
        {
            var hasOpen = await _db.ShiftLogs.AnyAsync(s => !s.IsClosed);
            if (hasOpen)
            {
                TempData["Warning"] = "Zaten açık bir vardiya mevcut. Önce kapatın.";
                // [S5-URL] Aynı controller → area explicit ama opsiyonel
                return RedirectToAction(nameof(Index), "Shift", new { area = "App" });
            }
            return View();
        }

        // ── POST /App/Shift/Open ──────────────────────────────────────────────
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
                TenantId = _tenantService.TenantId!   // [MT] izolasyon anahtarı
            };

            _db.ShiftLogs.Add(shift);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, shiftId = shift.ShiftLogId });
        }

        // ── GET /App/Shift/Detail/{id} ────────────────────────────────────────
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

        // ── GET /App/Shift/Close/{id} ─────────────────────────────────────────
        public async Task<IActionResult> Close(int id)
        {
            var shift = await _db.ShiftLogs
                .Include(s => s.OpenedByUser)
                .FirstOrDefaultAsync(s => s.ShiftLogId == id && !s.IsClosed);

            if (shift == null)
            {
                TempData["Error"] = "Vardiya bulunamadı veya zaten kapatılmış.";
                return RedirectToAction(nameof(Index), "Shift", new { area = "App" });
            }

            return View(shift);
        }

        // ── POST /App/Shift/Close/{id} ────────────────────────────────────────
        // Gövde: { closingBalance, notes }  — threshold opsiyonel
        //
        // [RC-03] Çift Vardiya Kapatma Race Condition → RepeatableRead TX
        // [P-02]  Tüm toplamlar DB SumAsync ile hesaplanır
        // [F-01]  İndirim DB aggregate farkından türetilir
        [HttpPost]
        public async Task<IActionResult> Close(int id, [FromBody] ShiftCloseDto dto)
        {
            var user = await _userManager.GetUserAsync(User);

            // [RC-03] TX ÖNCE açılır; shift bu TX içinde çekilir → satır kilitlenir
            using var tx = await _db.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.RepeatableRead);
            try
            {
                // [RC-03] Shift TX İÇİNDE sorgulanır
                var shift = await _db.ShiftLogs.FirstOrDefaultAsync(s => s.ShiftLogId == id);

                if (shift == null)
                    return NotFound(new { success = false, message = "Vardiya bulunamadı." });

                // [RC-03] Guard TX İÇİNDE — birinci commit sonrası ikinci istek buraya girer
                if (shift.IsClosed)
                    return BadRequest(new { success = false, message = "Vardiya zaten kapalı. Çift kapama engellendi." });

                var closedAt = DateTime.UtcNow;

                // ── [P-02] DB-Side SumAsync — hiçbir satır RAM'e çekilmez ────────
                decimal totalSales = await _db.Orders
                    .Where(o => o.OrderStatus == OrderStatus.Paid
                             && o.OrderClosedAt >= shift.OpenedAt
                             && o.OrderClosedAt <= closedAt)
                    .SumAsync(o => o.OrderTotalAmount);

                decimal totalCash = await _db.Payments
                    .Where(p => p.Order.OrderStatus == OrderStatus.Paid
                             && p.Order.OrderClosedAt >= shift.OpenedAt
                             && p.Order.OrderClosedAt <= closedAt
                             && p.PaymentsMethod == 0)
                    .SumAsync(p => p.PaymentsAmount);

                decimal totalCard = await _db.Payments
                    .Where(p => p.Order.OrderStatus == OrderStatus.Paid
                             && p.Order.OrderClosedAt >= shift.OpenedAt
                             && p.Order.OrderClosedAt <= closedAt
                             && (p.PaymentsMethod == 1 || p.PaymentsMethod == 2))
                    .SumAsync(p => p.PaymentsAmount);

                decimal totalOther = await _db.Payments
                    .Where(p => p.Order.OrderStatus == OrderStatus.Paid
                             && p.Order.OrderClosedAt >= shift.OpenedAt
                             && p.Order.OrderClosedAt <= closedAt
                             && p.PaymentsMethod == 3)
                    .SumAsync(p => p.PaymentsAmount);

                // [F-01] İndirim = GrossSatış − TümÖdemeler (DB aggregates)
                decimal totalPaid = totalCash + totalCard + totalOther;
                decimal totalDiscount = Math.Max(0m, totalSales - totalPaid);

                // ── [P-02] İptal & Zayi — SQL CASE WHEN ile tek sorgu ────────────
                decimal totalWaste = await _db.OrderItems
                    .Where(oi => oi.CancelledQuantity > 0
                              && (
                                  (oi.Order.OrderClosedAt != null
                                   && oi.Order.OrderClosedAt >= shift.OpenedAt
                                   && oi.Order.OrderClosedAt <= closedAt)
                                  ||
                                  (oi.Order.OrderClosedAt == null
                                   && oi.OrderItemAddedAt >= shift.OpenedAt
                                   && oi.OrderItemAddedAt <= closedAt)
                              ))
                    .SumAsync(oi => oi.CancelledQuantity *
                        (oi.OrderItemUnitPrice > 0
                            ? oi.OrderItemUnitPrice
                            : oi.MenuItem!.MenuItemPrice));

                // Kasa farkı: ClosingBalance - (OpeningBalance + TotalCash)
                decimal difference = dto.ClosingBalance - (shift.OpeningBalance + totalCash);

                // ── ShiftLog güncelle ────────────────────────────────────────────
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
                shift.IsClosed = true; // [RC-03] commit öncesi son adım

                await _db.SaveChangesAsync();
                await tx.CommitAsync(); // [RC-03]

                // ── SignalR: fark eşiği aşıldıysa uyarı gönder ──────────────────
                if (Math.Abs(difference) > shift.DifferenceThreshold)
                {
                    await _hub.Clients
                        .Group(_tenantService.TenantId ?? "")
                        .SendAsync("ShiftDifferenceAlert", new
                        {
                            shiftId = shift.ShiftLogId,
                            difference = shift.Difference,
                            threshold = shift.DifferenceThreshold,
                            closedBy = user.FullName ?? user.UserName
                        });
                }

                return Ok(new { success = true, shiftId = shift.ShiftLogId });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                return StatusCode(500, new { success = false, message = "Vardiya kapatılırken hata oluştu: " + ex.Message });
            }
        }

        // ── POST /App/Shift/ToggleLock/{id} ───────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> ToggleLock(int id)
        {
            var shift = await _db.ShiftLogs.FindAsync(id);
            if (shift == null) return NotFound(new { success = false });

            shift.IsLocked = !shift.IsLocked;
            await _db.SaveChangesAsync();

            return Ok(new { success = true, isLocked = shift.IsLocked });
        }

        // ── GET /App/Shift/PreviewTotals/{id} ────────────────────────────────
        // Close sayfası AJAX önizleme — [P-02] InMemory → DB SumAsync
        [HttpGet]
        public async Task<IActionResult> PreviewTotals(int id)
        {
            var shift = await _db.ShiftLogs.FirstOrDefaultAsync(s => s.ShiftLogId == id && !s.IsClosed);
            if (shift == null) return NotFound();

            var now = DateTime.UtcNow;

            decimal totalSales = await _db.Orders
                .Where(o => o.OrderStatus == OrderStatus.Paid
                         && o.OrderClosedAt >= shift.OpenedAt
                         && o.OrderClosedAt <= now)
                .SumAsync(o => o.OrderTotalAmount);

            decimal totalCash = await _db.Payments
                .Where(p => p.Order.OrderStatus == OrderStatus.Paid
                         && p.Order.OrderClosedAt >= shift.OpenedAt
                         && p.Order.OrderClosedAt <= now
                         && p.PaymentsMethod == 0)
                .SumAsync(p => p.PaymentsAmount);

            decimal totalCard = await _db.Payments
                .Where(p => p.Order.OrderStatus == OrderStatus.Paid
                         && p.Order.OrderClosedAt >= shift.OpenedAt
                         && p.Order.OrderClosedAt <= now
                         && (p.PaymentsMethod == 1 || p.PaymentsMethod == 2))
                .SumAsync(p => p.PaymentsAmount);

            decimal totalOther = await _db.Payments
                .Where(p => p.Order.OrderStatus == OrderStatus.Paid
                         && p.Order.OrderClosedAt >= shift.OpenedAt
                         && p.Order.OrderClosedAt <= now
                         && p.PaymentsMethod == 3)
                .SumAsync(p => p.PaymentsAmount);

            // [F-01] İndirim DB aggregate farkından
            decimal totalPaid = totalCash + totalCard + totalOther;
            decimal totalDiscount = Math.Max(0m, totalSales - totalPaid);

            decimal totalWaste = await _db.OrderItems
                .Where(oi => oi.CancelledQuantity > 0
                          && (
                              (oi.Order.OrderClosedAt != null
                               && oi.Order.OrderClosedAt >= shift.OpenedAt
                               && oi.Order.OrderClosedAt <= now)
                              ||
                              (oi.Order.OrderClosedAt == null
                               && oi.OrderItemAddedAt >= shift.OpenedAt
                               && oi.OrderItemAddedAt <= now)
                          ))
                .SumAsync(oi => oi.CancelledQuantity *
                    (oi.OrderItemUnitPrice > 0
                        ? oi.OrderItemUnitPrice
                        : oi.MenuItem!.MenuItemPrice));

            return Ok(new
            {
                totalSales,
                totalCash,
                totalCard,
                totalOther,
                totalDiscount,
                totalWaste,
                openingBalance = shift.OpeningBalance,
                differenceThreshold = shift.DifferenceThreshold
            });
        }

        // ── GET /App/Shift/GeneratePdf/{id} ──────────────────────────────────
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
                                c.Item().Text(vm.CancelledItemCount.ToString()).FontSize(14).Bold().FontColor("#f59e0b");
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
        // [P-02] Tüm parasal toplamlar + gruplama sorguları DB'de çalışır
        // [F-01] İndirim DB aggregate farkından türetilir
        // ═════════════════════════════════════════════════════════════════════
        private async Task<ShiftDetailViewModel> BuildDetailViewModel(ShiftLog shift)
        {
            var closedAt = shift.ClosedAt ?? DateTime.UtcNow;

            // ── [P-02] Ödeme dağılımı — DB SumAsync ────────────────────────────
            decimal cash = await _db.Payments
                .Where(p => p.Order.OrderStatus == OrderStatus.Paid
                         && p.Order.OrderClosedAt >= shift.OpenedAt
                         && p.Order.OrderClosedAt <= closedAt
                         && p.PaymentsMethod == 0)
                .SumAsync(p => p.PaymentsAmount);

            decimal creditCard = await _db.Payments
                .Where(p => p.Order.OrderStatus == OrderStatus.Paid
                         && p.Order.OrderClosedAt >= shift.OpenedAt
                         && p.Order.OrderClosedAt <= closedAt
                         && p.PaymentsMethod == 1)
                .SumAsync(p => p.PaymentsAmount);

            decimal debitCard = await _db.Payments
                .Where(p => p.Order.OrderStatus == OrderStatus.Paid
                         && p.Order.OrderClosedAt >= shift.OpenedAt
                         && p.Order.OrderClosedAt <= closedAt
                         && p.PaymentsMethod == 2)
                .SumAsync(p => p.PaymentsAmount);

            decimal other = await _db.Payments
                .Where(p => p.Order.OrderStatus == OrderStatus.Paid
                         && p.Order.OrderClosedAt >= shift.OpenedAt
                         && p.Order.OrderClosedAt <= closedAt
                         && p.PaymentsMethod == 3)
                .SumAsync(p => p.PaymentsAmount);

            // ── [P-02] İptal adedi — DB SumAsync ───────────────────────────────
            int cancelledCount = await _db.OrderItems
                .Where(oi => oi.CancelledQuantity > 0
                          && (
                              (oi.Order.OrderClosedAt != null
                               && oi.Order.OrderClosedAt >= shift.OpenedAt
                               && oi.Order.OrderClosedAt <= closedAt)
                              ||
                              (oi.Order.OrderClosedAt == null
                               && oi.OrderItemAddedAt >= shift.OpenedAt
                               && oi.OrderItemAddedAt <= closedAt)
                          ))
                .SumAsync(oi => oi.CancelledQuantity);

            // ── [P-02] Zayi tutarı — DB SumAsync + SQL CASE WHEN price fallback ─
            decimal wasteAmount = await _db.OrderItems
                .Where(oi => oi.CancelledQuantity > 0
                          && (
                              (oi.Order.OrderClosedAt != null
                               && oi.Order.OrderClosedAt >= shift.OpenedAt
                               && oi.Order.OrderClosedAt <= closedAt)
                              ||
                              (oi.Order.OrderClosedAt == null
                               && oi.OrderItemAddedAt >= shift.OpenedAt
                               && oi.OrderItemAddedAt <= closedAt)
                          ))
                .SumAsync(oi => oi.CancelledQuantity *
                    (oi.OrderItemUnitPrice > 0
                        ? oi.OrderItemUnitPrice
                        : oi.MenuItem!.MenuItemPrice));

            // ── [P-02] Garson bazlı satış — DB GroupBy ──────────────────────────
            var waiterSales = await _db.Orders
                .Where(o => o.OrderStatus == OrderStatus.Paid
                         && o.OrderClosedAt >= shift.OpenedAt
                         && o.OrderClosedAt <= closedAt)
                .GroupBy(o => o.OrderOpenedBy)
                .Select(g => new WaiterSalesRow
                {
                    WaiterName = g.Key ?? "Bilinmiyor",
                    OrderCount = g.Count(),
                    TotalAmount = g.Sum(o => o.OrderTotalAmount)
                })
                .OrderByDescending(w => w.TotalAmount)
                .ToListAsync();

            // ── [P-02] Top 5 ürün — DB GroupBy + Take(5) ───────────────────────
            var topProducts = await _db.OrderItems
                .Where(oi => oi.Order.OrderStatus == OrderStatus.Paid
                          && oi.Order.OrderClosedAt >= shift.OpenedAt
                          && oi.Order.OrderClosedAt <= closedAt
                          && oi.OrderItemQuantity > oi.CancelledQuantity)
                .GroupBy(oi => oi.MenuItem.MenuItemName)
                .Select(g => new TopProductRow
                {
                    ProductName = g.Key ?? "Bilinmiyor",
                    Quantity = g.Sum(oi => oi.OrderItemQuantity - oi.CancelledQuantity),
                    TotalAmount = g.Sum(oi => oi.OrderItemLineTotal)
                })
                .OrderByDescending(p => p.TotalAmount)
                .Take(5)
                .ToListAsync();

            // ── [P-02] Kategori dağılımı — DB GroupBy ──────────────────────────
            var categorySales = await _db.OrderItems
                .Where(oi => oi.Order.OrderStatus == OrderStatus.Paid
                          && oi.Order.OrderClosedAt >= shift.OpenedAt
                          && oi.Order.OrderClosedAt <= closedAt
                          && oi.OrderItemQuantity > oi.CancelledQuantity)
                .GroupBy(oi => oi.MenuItem.Category.CategoryName)
                .Select(g => new CategorySalesRow
                {
                    CategoryName = g.Key ?? "Diğer",
                    TotalAmount = g.Sum(oi => oi.OrderItemLineTotal)
                })
                .OrderByDescending(c => c.TotalAmount)
                .ToListAsync();

            return new ShiftDetailViewModel
            {
                ShiftLog = shift,
                TotalCash = cash,
                TotalCreditCard = creditCard,
                TotalDebitCard = debitCard,
                TotalOther = other,
                CancelledItemCount = cancelledCount,
                WasteAmount = wasteAmount,
                WaiterSales = waiterSales,
                TopProducts = topProducts,
                CategorySales = categorySales
            };
        }
    }
}
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Dtos.Tables;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Controllers
{
    [Authorize(Roles = "Admin,Garson,Kasiyer")]
    public class TablesController : Controller
    {
        private readonly RestaurantDbContext _db;
        private readonly IHubContext<RestaurantHub> _hub;

        public TablesController(RestaurantDbContext db, IHubContext<RestaurantHub> hub)
        {
            _db = db;
            _hub = hub;
        }
        // ── GET /Tables ───────────────────────────────────────────────
        public async Task<IActionResult> Index()
        {
            await CleanupExpiredReservationsAsync();

            ViewData["Title"] = "Masalar";
            ViewData["OccupiedCount"] = await _db.Tables.CountAsync(t => t.TableStatus == 1);

            var tables = await _db.Tables
                .Include(t => t.Orders.Where(o => o.OrderStatus == "open"))
                    .ThenInclude(o => o.OrderItems)
                        .ThenInclude(oi => oi.MenuItem)
                .ToListAsync();

            tables = tables
                .OrderBy(t => NaturalSortKey(t.TableName).prefix)
                .ThenBy(t => NaturalSortKey(t.TableName).number)
                .ThenBy(t => NaturalSortKey(t.TableName).suffix)
                .ToList();

            return View(tables);
        }

        private static (string prefix, int number, string suffix) NaturalSortKey(string name)
        {
            var m = Regex.Match(name ?? "", @"^(.*?)(\d+)(.*)$");
            if (m.Success)
                return (m.Groups[1].Value.ToLower(), int.Parse(m.Groups[2].Value), m.Groups[3].Value.ToLower());
            return ((name ?? "").ToLower(), 0, "");
        }

        private async Task CleanupExpiredReservationsAsync()
        {
            var cutoff = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(30));
            var expired = await _db.Tables
                .Where(t => t.TableStatus == 2
                         && t.ReservationTime.HasValue
                         && t.ReservationTime.Value <= cutoff)
                .ToListAsync();

            if (!expired.Any()) return;

            foreach (var t in expired)
            {
                t.TableStatus = 0; t.ReservationName = null;
                t.ReservationPhone = null; t.ReservationGuestCount = null; t.ReservationTime = null;
            }
            await _db.SaveChangesAsync();
        }

        // ── POST /Tables/Create ───────────────────────────────────────
        [Authorize(Roles = "Admin")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromBody] TableCreateDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TableName))
                return Json(new { success = false, message = "Masa adı boş olamaz." });

            if (dto.TableCapacity < 1 || dto.TableCapacity > 20)
                return Json(new { success = false, message = "Kapasite 1 ile 20 arasında olmalıdır." });

            if (await _db.Tables.AnyAsync(t => t.TableName == dto.TableName.Trim()))
                return Json(new { success = false, message = $"'{dto.TableName}' adında bir masa zaten var." });

            try
            {
                _db.Tables.Add(new Table
                {
                    TableName = dto.TableName.Trim(),
                    TableCapacity = dto.TableCapacity,
                    TableStatus = 0,
                    TableCreatedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();

                return Json(new { success = true, message = $"'{dto.TableName.Trim()}' başarıyla eklendi.", redirectUrl = Url.Action(nameof(Index)) });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Masa eklenirken hata oluştu: " + ex.Message });
            }
        }

        // ── POST /Tables/Reserve ──────────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Reserve([FromBody] TableReserveDto dto)
        {
            var table = await _db.Tables.FindAsync(dto.TableId);
            if (table == null)
                return Json(new { success = false, message = "Masa bulunamadı." });
            if (table.TableStatus != 0)
                return Json(new { success = false, message = "Yalnızca boş masalar rezerve edilebilir." });
            if (string.IsNullOrWhiteSpace(dto.ReservationName))
                return Json(new { success = false, message = "İsim soyisim boş olamaz." });
            if (string.IsNullOrWhiteSpace(dto.ReservationPhone))
                return Json(new { success = false, message = "Telefon numarası boş olamaz." });
            if (dto.ReservationGuestCount < 1 || dto.ReservationGuestCount > table.TableCapacity)
                return Json(new { success = false, message = $"Kişi sayısı 1 ile {table.TableCapacity} arasında olmalıdır." });
            if (!TimeSpan.TryParse(dto.ReservationTime, out TimeSpan parsedTime))
                return Json(new { success = false, message = "Geçerli bir rezervasyon saati giriniz." });

            var localNow = DateTime.Now;
            var reservationLocal = localNow.Date.Add(parsedTime);
            if (reservationLocal < localNow.AddMinutes(-5))
                return Json(new { success = false, message = "Rezervasyon saati geçmiş bir saat olamaz." });

            var reservationUtc = DateTime.SpecifyKind(reservationLocal, DateTimeKind.Local).ToUniversalTime();

            // ── ÇELİŞME KONTROLÜ: Aynı müşteri, aynı gün + aynı saat ──────────────
            // Sadece Pending (TableStatus == 2) rezervasyonlar kontrol edilir.
            // İptal edilmiş (TableStatus == 0) masalardaki geçmiş rezervasyonlar kapsam dışı.
            var sameDayStart = reservationUtc.Date;
            var sameDayEnd = sameDayStart.AddDays(1);
            var normalizedPhone = dto.ReservationPhone.Trim();
            var normalizedName = dto.ReservationName.Trim();

            // ReservationTime aynı saat olup olmadığını UTC bazında karşılaştır (dakika hassasiyeti)
            var reservationHourMinute = new TimeSpan(reservationUtc.Hour, reservationUtc.Minute, 0);

            var isDuplicate = await _db.Tables.AnyAsync(t =>
                t.TableStatus == 2 &&                                    // Sadece aktif rezervasyonlar
                t.TableId != dto.TableId &&                              // Farklı masa olabilir
                t.ReservationPhone == normalizedPhone &&
                
                t.ReservationTime.HasValue &&
                t.ReservationTime.Value >= sameDayStart &&
                t.ReservationTime.Value < sameDayEnd &&
                t.ReservationTime.Value.Hour == reservationUtc.Hour &&
                t.ReservationTime.Value.Minute == reservationUtc.Minute);

            if (isDuplicate)
                return Json(new
                {
                    success = false,
                    isDuplicate = true,
                    message = "Bu telefon numarasıyla seçtiğiniz tarih ve saat için zaten adınıza kayıtlı bir rezervasyon bulunmaktadır."
                });
            // ──────────────────────────────────────────────────────────────────────

            try
            {
                table.TableStatus = 2;
                table.ReservationName = normalizedName;
                table.ReservationPhone = normalizedPhone;
                table.ReservationGuestCount = dto.ReservationGuestCount;
                table.ReservationTime = reservationUtc;

                await _db.SaveChangesAsync();
                return Json(new { success = true, message = $"'{table.TableName}' — {dto.ReservationName} adına rezerve edildi.", redirectUrl = Url.Action(nameof(Index)) });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Rezervasyon kaydedilirken hata oluştu: " + ex.Message });
            }
        }

        // ── POST /Tables/CancelReserve ────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelReserve([FromBody] TableReserveDto dto)
        {
            var table = await _db.Tables.FindAsync(dto.TableId);
            if (table == null)
                return Json(new { success = false, message = "Masa bulunamadı." });
            if (table.TableStatus != 2)
                return Json(new { success = false, message = "Bu masa zaten rezerve değil." });

            try
            {
                table.TableStatus = 0; table.ReservationName = null;
                table.ReservationPhone = null; table.ReservationGuestCount = null; table.ReservationTime = null;

                await _db.SaveChangesAsync();
                return Json(new { success = true, message = $"'{table.TableName}' rezervasyonu iptal edildi.", redirectUrl = Url.Action(nameof(Index)) });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Rezervasyon iptal edilirken hata oluştu: " + ex.Message });
            }
        }

        // ── POST /Tables/Delete ───────────────────────────────────────
        [Authorize(Roles = "Admin")]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete([FromBody] TableDeleteDto dto)
        {
            var table = await _db.Tables.FindAsync(dto.TableId);
            if (table == null)
                return Json(new { success = false, message = "Masa bulunamadı." });
            if (table.TableStatus == 1)
                return Json(new { success = false, message = "Açık adisyonu olan masa silinemez." });

            try
            {
                _db.Tables.Remove(table);
                await _db.SaveChangesAsync();
                return Json(new { success = true, message = $"'{table.TableName}' silindi.", redirectUrl = Url.Action(nameof(Index)) });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Masa silinirken hata oluştu: " + ex.Message });
            }
        }

        // ── POST /Tables/MergeOrder ───────────────────────────────────
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> MergeOrder([FromBody] TableMergeOrderDto dto)
        {
            if (dto.SourceTableId == dto.TargetTableId)
                return Json(new { success = false, message = "Kaynak ve hedef masa aynı olamaz." });

            var sourceOrder = await _db.Orders
                .Include(o => o.OrderItems)
                .Include(o => o.Payments)
                .Include(o => o.Table)
                .FirstOrDefaultAsync(o => o.TableId == dto.SourceTableId && o.OrderStatus == "open");

            if (sourceOrder == null)
                return Json(new { success = false, message = "Kaynak masada açık adisyon bulunamadı." });

            var targetTable = await _db.Tables.FindAsync(dto.TargetTableId);
            if (targetTable == null)
                return Json(new { success = false, message = "Hedef masa bulunamadı." });

            using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var targetOrder = await _db.Orders
                    .Include(o => o.OrderItems)
                    .FirstOrDefaultAsync(o => o.TableId == dto.TargetTableId && o.OrderStatus == "open");

                if (targetOrder == null)
                {
                    sourceOrder.TableId = dto.TargetTableId;
                    sourceOrder.Table.TableStatus = 0;
                    targetTable.TableStatus = 1;
                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Json(new
                    {
                        success = true,
                        message = $"Adisyon '{targetTable.TableName}' masasına taşındı.",
                        //redirectUrl = Url.Action("Detail", "Orders", new { id = sourceOrder.OrderId })//burası direk order detail akranına yönlendirir
                        redirectUrl = Url.Action(nameof(Index)) // BURASI DEĞİŞTİ
                    });
                }

                foreach (var srcItem in sourceOrder.OrderItems)
                {
                    var existing = targetOrder.OrderItems
                        .FirstOrDefault(ti => ti.MenuItemId == srcItem.MenuItemId);

                    if (existing != null)
                    {
                        existing.OrderItemQuantity += srcItem.OrderItemQuantity;
                        existing.OrderItemLineTotal += srcItem.OrderItemLineTotal;
                        if (StatusPriority(srcItem.OrderItemStatus) < StatusPriority(existing.OrderItemStatus))
                            existing.OrderItemStatus = srcItem.OrderItemStatus;
                        _db.OrderItems.Remove(srcItem);
                    }
                    else
                    {
                        srcItem.OrderId = targetOrder.OrderId;
                    }
                }

                foreach (var payment in sourceOrder.Payments)
                    payment.OrderId = targetOrder.OrderId;

                await _db.SaveChangesAsync();
                targetOrder.OrderTotalAmount = await _db.OrderItems
                    .Where(oi => oi.OrderId == targetOrder.OrderId && oi.OrderItemStatus != "cancelled")
                    .SumAsync(oi => oi.OrderItemLineTotal);

                sourceOrder.OrderStatus = "cancelled";
                sourceOrder.OrderClosedAt = DateTime.UtcNow;
                sourceOrder.OrderTotalAmount = 0;
                sourceOrder.Table.TableStatus = 0;
                targetTable.TableStatus = 1;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                return Json(new
                {
                    success = true,
                    message = $"'{sourceOrder.Table.TableName}' adisyonu '{targetTable.TableName}' masasına birleştirildi.",
                    //redirectUrl = Url.Action("Detail", "Orders", new { id = targetOrder.OrderId })
                    redirectUrl = Url.Action(nameof(Index)) // BURASI DEĞİŞTİ
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return Json(new { success = false, message = "Birleştirme sırasında hata oluştu: " + ex.Message });
            }
        }



        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DismissWaiter([FromBody] DismissWaiterDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.TableName))
                return Json(new { success = false, message = "Geçersiz masa adı." });

            var table = await _db.Tables
                .FirstOrDefaultAsync(t => t.TableName == dto.TableName);

            if (table == null)
                return Json(new { success = false, message = "Masa bulunamadı." });

            table.IsWaiterCalled = false;
            await _db.SaveChangesAsync();

            // Tüm bağlı ekranlara "artık normal" sinyali gönder
            await _hub.Clients.All.SendAsync("WaiterDismissed", new
            {
                tableName = table.TableName
            });

            return Json(new { success = true });
        }


        private static int StatusPriority(string status) => status switch
        {
            "pending" => 0,
            "preparing" => 1,
            "served" => 2,
            _ => 3
        };
    }
}

// ============================================================================
//  Data/RestaurantDbContext.cs
//  DEĞİŞİKLİK — FAZ 1 FİNAL: Enum Converter + StockLog Restrict
//
//  EKLENEN / DEĞİŞTİRİLEN:
//  [ENUM-CONV-1] Order.OrderStatus      → Value Converter (enum ↔ "open"|"paid"|"cancelled")
//  [ENUM-CONV-2] OrderItem.OrderItemStatus → Value Converter (enum ↔ "pending"|...)
//  [RESTRICT]    StockLog → MenuItem   OnDelete: Cascade → Restrict
//                Ürün silindiğinde stok geçmişi KORUNUR (audit kaydı)
//
//  KORUNAN:
//  - Tüm Global Query Filter'lar (Multi-Tenancy)
//  - ShiftLog konfigürasyonu
//  - Diğer tüm OnDelete, HasMaxLength, HasPrecision ayarları
// ============================================================================
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared.Common;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data
{
    public class RestaurantDbContext : IdentityDbContext<ApplicationUser>
    {
        // ── DbSet'ler ─────────────────────────────────────────────────────────
        public DbSet<Table> Tables { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<MenuItem> MenuItems { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<StockLog> StockLogs { get; set; }
        public DbSet<ShiftLog> ShiftLogs { get; set; }
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<TenantConfig> TenantConfigs { get; set; }

        // ── ITenantService ────────────────────────────────────────────────────
        private readonly ITenantService _tenantService;

        public RestaurantDbContext(
            DbContextOptions options,
            ITenantService tenantService)
            : base(options)
        {
            _tenantService = tenantService;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ════════════════════════════════════════════════════════════════
            //  [ENUM-CONV-1] OrderStatus Value Converter
            //
            //  Amaç: C# enum ↔ DB lowercase string dönüşümü
            //  Neden: KDS ve QR Menü JS frontend'leri string bekliyor.
            //         Enum eklenirken DB şeması ve JS BOZMADAN tip güvenliği
            //         kazanılır.
            //
            //  Dönüşüm tablosu:
            //    OrderStatus.Open      ↔ "open"
            //    OrderStatus.Paid      ↔ "paid"
            //    OrderStatus.Cancelled ↔ "cancelled"
            // ════════════════════════════════════════════════════════════════
            var orderStatusConverter = new ValueConverter<OrderStatus, string>(
                enumVal => enumVal.ToString().ToLowerInvariant(),          // C# → DB
                dbStr => Enum.Parse<OrderStatus>(dbStr, true)              // DB → C# (ignoreCase isimlendirmesi kaldırıldı)
            );

            // ════════════════════════════════════════════════════════════════
            //  [ENUM-CONV-2] OrderItemStatus Value Converter
            //
            //  Dönüşüm tablosu:
            //    OrderItemStatus.Pending    ↔ "pending"
            //    OrderItemStatus.Preparing  ↔ "preparing"
            //    OrderItemStatus.Ready      ↔ "ready"
            //    OrderItemStatus.Served     ↔ "served"
            //    OrderItemStatus.Cancelled  ↔ "cancelled"
            // ════════════════════════════════════════════════════════════════
            var orderItemStatusConverter = new ValueConverter<OrderItemStatus, string>(
                enumVal => enumVal.ToString().ToLowerInvariant(),
                dbStr => Enum.Parse<OrderItemStatus>(dbStr, true)          // (ignoreCase isimlendirmesi kaldırıldı)
            );

            // ════════════════════════════════════════════════════════════════
            //  Tenant & TenantConfig
            // ════════════════════════════════════════════════════════════════
            modelBuilder.Entity<Tenant>(entity =>
            {
                entity.ToTable("tenants");
                entity.HasKey(t => t.TenantId);
                entity.Property(t => t.TenantId).HasMaxLength(100).IsRequired();
                entity.Property(t => t.Name).HasMaxLength(200).IsRequired();
                entity.Property(t => t.Subdomain).HasMaxLength(100).IsRequired();
                entity.Property(t => t.PlanType).HasMaxLength(20).HasDefaultValue("trial").IsRequired();
                entity.Property(t => t.IsActive).HasDefaultValue(true).IsRequired();
                entity.Property(t => t.CreatedAt).HasDefaultValueSql("NOW()").IsRequired();
                entity.Property(t => t.TrialEndsAt);
                entity.Property(t => t.RestaurantType)
                    .HasConversion<int>()
                    .IsRequired();
                entity.HasIndex(t => t.Subdomain)
                    .IsUnique()
                    .HasDatabaseName("uq_tenants_subdomain");
                entity.HasOne(t => t.Config)
                    .WithOne(c => c.Tenant)
                    .HasForeignKey<TenantConfig>(c => c.TenantId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<TenantConfig>(entity =>
            {
                entity.ToTable("tenant_configs");
                entity.HasKey(c => c.TenantId);
                entity.Property(c => c.TenantId).HasMaxLength(100).IsRequired();
                entity.Property(c => c.CurrencyCode).HasMaxLength(3).HasDefaultValue("TRY").IsRequired();
                entity.Property(c => c.TaxRate).HasPrecision(5, 2);
                entity.Property(c => c.LogoPath).HasMaxLength(500);
                entity.Property(c => c.PrimaryColor).HasMaxLength(7);
                entity.Property(c => c.RestaurantDisplayName).HasMaxLength(200);
                entity.Property(c => c.EnableKitchenDisplay).HasDefaultValue(true).IsRequired();
                entity.Property(c => c.EnableTableMerge).HasDefaultValue(false).IsRequired();
                entity.Property(c => c.EnableSelfOrderQr).HasDefaultValue(false).IsRequired();
                entity.Property(c => c.EnableCourseManagement).HasDefaultValue(false).IsRequired();
                entity.Property(c => c.EnableReservations).HasDefaultValue(true).IsRequired();
                entity.Property(c => c.EnableSplitBill).HasDefaultValue(false).IsRequired();
                entity.Property(c => c.EnableDiscounts).HasDefaultValue(true).IsRequired();
                entity.Property(c => c.EnableLoyaltyProgram).HasDefaultValue(false).IsRequired();
                entity.Property(c => c.EnableMultiBranch).HasDefaultValue(false).IsRequired();
            });

            // ════════════════════════════════════════════════════════════════
            //  ApplicationUser → Tenant
            // ════════════════════════════════════════════════════════════════
            modelBuilder.Entity<ApplicationUser>(entity =>
            {
                entity.Property(u => u.TenantId).HasMaxLength(100);
                entity.HasOne(u => u.Tenant)
                    .WithMany(t => t.Users)
                    .HasForeignKey(u => u.TenantId)
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired(false);
            });

            // ════════════════════════════════════════════════════════════════
            //  Table — Global Query Filter
            // ════════════════════════════════════════════════════════════════
            modelBuilder.Entity<Table>(entity =>
            {
                entity.ToTable("tables");
                entity.HasKey(t => t.TableId);
                entity.Property(t => t.TableName).HasMaxLength(50).IsRequired();
                entity.Property(t => t.TableCapacity).HasMaxLength(2).HasDefaultValue(4).IsRequired();
                entity.Property(t => t.TableStatus).HasDefaultValue(0).IsRequired();
                entity.Property(t => t.TableCreatedAt).HasDefaultValueSql("NOW()").IsRequired();
                entity.Property(t => t.TenantId).HasMaxLength(100).IsRequired();
                entity.HasOne(t => t.Tenant)
                    .WithMany()
                    .HasForeignKey(t => t.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasQueryFilter(t =>
                    _tenantService.TenantId == null ||
                    t.TenantId == _tenantService.TenantId);
            });

            // ════════════════════════════════════════════════════════════════
            //  Category — Global Query Filter
            // ════════════════════════════════════════════════════════════════
            modelBuilder.Entity<Category>(entity =>
            {
                entity.ToTable("categories");
                entity.HasKey(c => c.CategoryId);
                entity.Property(c => c.CategoryName).HasMaxLength(100).IsRequired();
                entity.Property(c => c.CategorySortOrder).HasDefaultValue(0).IsRequired();
                entity.Property(c => c.IsActive).HasDefaultValue(true).IsRequired();
                entity.HasIndex(c => new { c.TenantId, c.CategoryName })
                    .IsUnique()
                    .HasDatabaseName("uq_categories_tenant_name");
                entity.Property(c => c.TenantId).HasMaxLength(100).IsRequired();
                entity.HasOne(c => c.Tenant)
                    .WithMany()
                    .HasForeignKey(c => c.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasQueryFilter(c =>
                    _tenantService.TenantId == null ||
                    c.TenantId == _tenantService.TenantId);
            });

            // ════════════════════════════════════════════════════════════════
            //  MenuItem — Global Query Filter
            // ════════════════════════════════════════════════════════════════
            modelBuilder.Entity<MenuItem>(entity =>
            {
                entity.ToTable("menu_items");
                entity.HasKey(m => m.MenuItemId);
                entity.Property(m => m.MenuItemName).HasMaxLength(200).IsRequired();
                entity.Property(m => m.MenuItemPrice).HasPrecision(10, 2).IsRequired();
                entity.Property(m => m.StockQuantity).HasDefaultValue(0).IsRequired();
                entity.Property(m => m.AlertThreshold).HasDefaultValue(0).IsRequired();
                entity.Property(m => m.CriticalThreshold).HasDefaultValue(0).IsRequired();
                entity.Property(m => m.CostPrice).HasPrecision(10, 2);
                entity.Property(m => m.TrackStock).HasDefaultValue(false).IsRequired();
                entity.Property(m => m.IsAvailable).HasDefaultValue(true).IsRequired();
                entity.Property(m => m.Description).HasColumnType("text");
                entity.Property(m => m.MenuItemCreatedTime).HasDefaultValueSql("NOW()").IsRequired();
                entity.HasOne(c => c.Category)
                    .WithMany(m => m.MenuItems)
                    .HasForeignKey(c => c.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.Property(m => m.TenantId).HasMaxLength(100).IsRequired();
                entity.HasOne(m => m.Tenant)
                    .WithMany()
                    .HasForeignKey(m => m.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasQueryFilter(m =>
                    _tenantService.TenantId == null ||
                    m.TenantId == _tenantService.TenantId);
            });

            // ════════════════════════════════════════════════════════════════
            //  Order — Global Query Filter + [ENUM-CONV-1] Value Converter
            // ════════════════════════════════════════════════════════════════
            modelBuilder.Entity<Order>(entity =>
            {
                entity.ToTable("orders");
                entity.HasKey(o => o.OrderId);

                // [ENUM-CONV-1] OrderStatus enum → "open"/"paid"/"cancelled" string
                // HasDefaultValue burada DB default değerini belirtir; converter bunu
                // enum'a çevirmez (seed değeri doğrudan DB'ye yazılır).
                entity.Property(o => o.OrderStatus)
                    .HasConversion(orderStatusConverter)
                    .HasMaxLength(20)
                    .HasDefaultValueSql("'open'")
                    .IsRequired();

                entity.Property(o => o.OrderOpenedBy).HasMaxLength(100);
                entity.Property(o => o.OrderNote).HasColumnType("text");
                entity.Property(o => o.OrderTotalAmount).HasPrecision(12, 2).HasDefaultValue(0).IsRequired();
                entity.Property(o => o.OrderOpenedAt).HasDefaultValueSql("NOW()").IsRequired();
                entity.HasOne(t => t.Table)
                    .WithMany(o => o.Orders)
                    .HasForeignKey(o => o.TableId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.Property(o => o.TenantId).HasMaxLength(100).IsRequired();
                entity.HasOne(o => o.Tenant)
                    .WithMany()
                    .HasForeignKey(o => o.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasQueryFilter(o =>
                    _tenantService.TenantId == null ||
                    o.TenantId == _tenantService.TenantId);
            });

            // ════════════════════════════════════════════════════════════════
            //  OrderItem — [ENUM-CONV-2] Value Converter
            //  [WARN-1 DÜZELTME] MenuItem üzerinden Global Query Filter
            // ════════════════════════════════════════════════════════════════
            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.ToTable("order_items");
                entity.HasKey(o => o.OrderItemId);
                entity.Property(o => o.OrderItemQuantity).IsRequired();
                entity.Property(o => o.PaidQuantity).IsRequired().HasDefaultValue(0);
                entity.Property(o => o.OrderItemUnitPrice).HasPrecision(10, 2).IsRequired();
                entity.Property(o => o.OrderItemLineTotal).HasPrecision(12, 2).IsRequired();
                entity.Property(o => o.OrderItemNote).HasColumnType("text");

                // [ENUM-CONV-2] OrderItemStatus enum → "pending"/"preparing"/... string
                entity.Property(o => o.OrderItemStatus)
                    .HasConversion(orderItemStatusConverter)
                    .HasMaxLength(20)
                    .HasDefaultValueSql("pending")
                    .IsRequired();

                entity.Property(o => o.OrderItemAddedAt).HasDefaultValueSql("NOW()").IsRequired();
                entity.Property(o => o.CancelledQuantity).IsRequired().HasDefaultValue(0);
                entity.Property(o => o.CancelReason).HasColumnType("text");
                entity.Property(o => o.IsWasted).IsRequired(false);
                entity.Ignore(o => o.ActiveQuantity);
                entity.Ignore(o => o.RemainingQuantity);
                entity.Ignore(o => o.UnpaidLineTotal);
                entity.Ignore(o => o.PaidLineTotal);
                entity.Ignore(o => o.CancelledLineTotal);
                entity.HasOne(o => o.Order)
                    .WithMany(o => o.OrderItems)
                    .HasForeignKey(o => o.OrderId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(o => o.MenuItem)
                    .WithMany()
                    .HasForeignKey(o => o.MenuItemId)
                    .OnDelete(DeleteBehavior.Restrict);

                // [WARN-1 DÜZELTME] MenuItem filtresiyle eşleşen filtre
                entity.HasQueryFilter(o =>
                    _tenantService.TenantId == null ||
                    o.MenuItem.TenantId == _tenantService.TenantId);
            });

            // ════════════════════════════════════════════════════════════════
            //  Payment
            //  [WARN-2 DÜZELTME] Order üzerinden Global Query Filter
            // ════════════════════════════════════════════════════════════════
            modelBuilder.Entity<Payment>(entity =>
            {
                entity.ToTable("payments");
                entity.HasKey(p => p.PaymentId);
                entity.Property(p => p.PaymentsMethod).IsRequired().HasDefaultValue(0);
                entity.Property(p => p.PaymentsAmount).HasDefaultValue(0).HasPrecision(10, 2).IsRequired();
                entity.Property(p => p.PaymentsChangeGiven).HasDefaultValue(0).HasPrecision(10, 2).IsRequired();
                entity.Property(p => p.PaymentsPaidAt).HasDefaultValueSql("NOW()").IsRequired();
                entity.Property(p => p.PaymentsNote).IsRequired().HasColumnType("text");
                entity.HasOne(o => o.Order)
                    .WithMany(p => p.Payments)
                    .HasForeignKey(o => o.OrderId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasQueryFilter(p =>
                    _tenantService.TenantId == null ||
                    p.Order.TenantId == _tenantService.TenantId);
            });

            // ════════════════════════════════════════════════════════════════
            //  StockLog
            //  [WARN-3 DÜZELTME] MenuItem üzerinden Global Query Filter
            //  [RESTRICT] OnDelete: Cascade → Restrict
            //
            //  GEREKÇE: Ürün silindiğinde stok geçmişi (StockLog) kayıtları
            //  SİLİNMEMELİ. Bu kayıtlar mali ve audit amaçlı tutulur.
            //  Cascade ile tüm stok geçmişi kayboluyordu — kritik veri kaybı.
            //
            //  YENİ DAVRANIM: MenuItem.IsDeleted = true (soft-delete) yapılır,
            //  fiziksel silme yapılmaz. StockLog'lar korunur.
            // ════════════════════════════════════════════════════════════════
            modelBuilder.Entity<StockLog>(entity =>
            {
                entity.ToTable("stock_logs");
                entity.HasKey(s => s.StockLogId);
                entity.Property(s => s.MovementType).HasMaxLength(20).IsRequired();
                entity.Property(s => s.QuantityChange).IsRequired();
                entity.Property(s => s.PreviousStock).IsRequired();
                entity.Property(s => s.NewStock).IsRequired();
                entity.Property(s => s.Note).HasColumnType("text");
                entity.Property(s => s.CreatedAt).HasDefaultValueSql("NOW()").IsRequired();
                entity.Property(s => s.SourceType).HasMaxLength(30);
                entity.Property(s => s.OrderId);
                entity.Property(s => s.UnitPrice).HasColumnType("numeric(18,2)");

                // [RESTRICT] Cascade → Restrict: ürün silinse bile stok geçmişi korunur
                entity.HasOne(s => s.MenuItem)
                    .WithMany()
                    .HasForeignKey(s => s.MenuItemId)
                    .OnDelete(DeleteBehavior.Restrict);

                // [WARN-3 DÜZELTME] MenuItem filtresiyle eşleşen filtre
                entity.HasQueryFilter(s =>
                    _tenantService.TenantId == null ||
                    s.MenuItem.TenantId == _tenantService.TenantId);
            });

            // ════════════════════════════════════════════════════════════════
            //  ShiftLog — Kasa Vardiyası (Faz 1 Adım 3)
            //  [MT] TenantId FK + Global Query Filter
            // ════════════════════════════════════════════════════════════════
            modelBuilder.Entity<ShiftLog>(entity =>
            {
                entity.ToTable("shift_logs");
                entity.HasKey(s => s.ShiftLogId);
                entity.Property(s => s.OpenedAt).IsRequired();
                entity.Property(s => s.OpenedByUserId).HasMaxLength(450).IsRequired();
                entity.Property(s => s.ClosedByUserId).HasMaxLength(450);
                entity.Property(s => s.OpeningBalance).HasPrecision(12, 2).IsRequired();
                entity.Property(s => s.ClosingBalance).HasPrecision(12, 2).IsRequired();
                entity.Property(s => s.TotalSales).HasPrecision(12, 2).IsRequired();
                entity.Property(s => s.TotalCash).HasPrecision(12, 2).IsRequired();
                entity.Property(s => s.TotalCard).HasPrecision(12, 2).IsRequired();
                entity.Property(s => s.TotalOther).HasPrecision(12, 2).IsRequired();
                entity.Property(s => s.TotalDiscount).HasPrecision(12, 2).IsRequired();
                entity.Property(s => s.TotalWaste).HasPrecision(12, 2).IsRequired();
                entity.Property(s => s.Difference).HasPrecision(12, 2).IsRequired();
                entity.Property(s => s.DifferenceThreshold).HasPrecision(12, 2).HasDefaultValue(100m).IsRequired();
                entity.Property(s => s.Notes).HasColumnType("text");
                entity.Property(s => s.IsClosed).HasDefaultValue(false).IsRequired();
                entity.Property(s => s.IsLocked).HasDefaultValue(false).IsRequired();
                entity.HasOne(s => s.OpenedByUser)
                    .WithMany()
                    .HasForeignKey(s => s.OpenedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(s => s.ClosedByUser)
                    .WithMany()
                    .HasForeignKey(s => s.ClosedByUserId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.Property(s => s.TenantId).HasMaxLength(100).IsRequired();
                entity.HasOne(s => s.Tenant)
                    .WithMany()
                    .HasForeignKey(s => s.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);
                entity.HasQueryFilter(s =>
                    _tenantService.TenantId == null ||
                    s.TenantId == _tenantService.TenantId);
            });
        }
    }
}
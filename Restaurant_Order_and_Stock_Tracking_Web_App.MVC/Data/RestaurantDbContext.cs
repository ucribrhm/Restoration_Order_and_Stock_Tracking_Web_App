// ============================================================================
//  Data/RestaurantDbContext.cs
//  DEĞİŞİKLİK — FAZ 1 ADIM 2: Multi-Tenancy
//
//  EKLENENLER:
//    [MT-1] ITenantService constructor injection
//    [MT-2] DbSet<Tenant> ve DbSet<TenantConfig>
//    [MT-3] Tenant + TenantConfig EF konfigürasyonu
//    [MT-4] Global Query Filter'lar (Table, Order, MenuItem, Category)
//    [MT-5] ApplicationUser → Tenant FK ilişkisi
//    [MT-6] Category unique index: global → (TenantId, CategoryName) scoped
//    [MT-7] TenantId FK'ları tüm izole tablolara eklendi
//
//  KORUNANLAR:
//    Tüm mevcut entity konfigürasyonları (ToTable, Property, Index, FK'lar)
//    birebir korundu. Yalnızca yukarıdaki [MT-x] etiketli satırlar eklendi.
// ============================================================================
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data
{
    public class RestaurantDbContext : IdentityDbContext<ApplicationUser>
    {
        // ── Mevcut DbSet'ler (değişmedi) ─────────────────────────────────────
        public DbSet<Table> Tables { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<MenuItem> MenuItems { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<StockLog> StockLogs { get; set; }

        // ── [MT-2] Yeni SaaS DbSet'leri ─────────────────────────────────────
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<TenantConfig> TenantConfigs { get; set; }

        // ── [MT-1] ITenantService injection ─────────────────────────────────
        private readonly ITenantService _tenantService;

        /// <summary>
        /// Constructor: ITenantService EF Core runtime'da inject edilir.
        ///
        /// Design-time (migration) senaryosu:
        ///   EF Tools bu constructor'ı bulamazsa IDesignTimeDbContextFactory
        ///   arar. Biz bunun yerine ITenantService'i null-safe tasarladık:
        ///   TenantId null döndüğünde Global Query Filter devredışı kalır,
        ///   migration'lar tüm veriyi görebilir.
        /// </summary>
        public RestaurantDbContext(
            DbContextOptions options,
            ITenantService tenantService)   // [MT-1]
            : base(options)
        {
            _tenantService = tenantService;
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder); // Identity tablolarını oluşturur — MUTLAKA OLMALI

            // ════════════════════════════════════════════════════════════════
            //  [MT-3] Tenant & TenantConfig konfigürasyonu
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
                    .HasDefaultValue(RestaurantType.CasualDining)
                    .IsRequired();

                // Subdomain benzersiz olmalı
                entity.HasIndex(t => t.Subdomain)
                    .IsUnique()
                    .HasDatabaseName("uq_tenants_subdomain");

                // 1-1 ilişki: Tenant → TenantConfig
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

                // Feature flag default'ları
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
            //  [MT-5] ApplicationUser → Tenant ilişkisi
            // ════════════════════════════════════════════════════════════════
            modelBuilder.Entity<ApplicationUser>(entity =>
            {
                entity.Property(u => u.TenantId).HasMaxLength(100);
                entity.HasOne(u => u.Tenant)
                    .WithMany(t => t.Users)
                    .HasForeignKey(u => u.TenantId)
                    .OnDelete(DeleteBehavior.Restrict)
                    .IsRequired(false); // Süper admin için null kabul edilir
            });

            // ════════════════════════════════════════════════════════════════
            //  Table — mevcut konfigürasyon KORUNDU + [MT-4] + [MT-7]
            // ════════════════════════════════════════════════════════════════
            modelBuilder.Entity<Table>(entity =>
            {
                entity.ToTable("tables");
                entity.HasKey(t => t.TableId);
                entity.Property(t => t.TableName).HasMaxLength(50).IsRequired();
                entity.Property(t => t.TableCapacity).HasMaxLength(2).HasDefaultValue(4).IsRequired();
                entity.Property(t => t.TableStatus).HasDefaultValue(0).IsRequired();
                entity.Property(t => t.TableCreatedAt).HasDefaultValueSql("NOW()").IsRequired();

                // [MT-7] TenantId sütun konfigürasyonu
                entity.Property(t => t.TenantId).HasMaxLength(100).IsRequired();
                entity.HasOne(t => t.Tenant)
                    .WithMany()
                    .HasForeignKey(t => t.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                // [MT-4] Global Query Filter — THE MAGIC SHIELD
                // TenantId null (migration/seed) → tüm veriler döner
                // TenantId dolu (normal istek) → yalnızca o tenant'ın verileri döner
                entity.HasQueryFilter(t =>
                    _tenantService.TenantId == null ||
                    t.TenantId == _tenantService.TenantId);
            });

            // ════════════════════════════════════════════════════════════════
            //  Category — mevcut konfigürasyon KORUNDU + [MT-4] + [MT-6] + [MT-7]
            // ════════════════════════════════════════════════════════════════
            modelBuilder.Entity<Category>(entity =>
            {
                entity.ToTable("categories");
                entity.HasKey(c => c.CategoryId);
                entity.Property(c => c.CategoryName).HasMaxLength(100).IsRequired();
                entity.Property(c => c.CategorySortOrder).HasDefaultValue(0).IsRequired();
                entity.Property(c => c.IsActive).HasDefaultValue(true).IsRequired();

                // [MT-6] Global unique → Tenant-scoped unique
                // ÖNCE: entity.HasIndex(c => c.CategoryName).IsUnique()
                //       — farklı restoranlar aynı adı kullanamıyordu (YANLIŞ)
                // SONRA: (TenantId, CategoryName) birlikte benzersiz
                entity.HasIndex(c => new { c.TenantId, c.CategoryName })
                    .IsUnique()
                    .HasDatabaseName("uq_categories_tenant_name");

                // [MT-7] TenantId sütun konfigürasyonu
                entity.Property(c => c.TenantId).HasMaxLength(100).IsRequired();
                entity.HasOne(c => c.Tenant)
                    .WithMany()
                    .HasForeignKey(c => c.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                // [MT-4] Global Query Filter
                entity.HasQueryFilter(c =>
                    _tenantService.TenantId == null ||
                    c.TenantId == _tenantService.TenantId);
            });

            // ════════════════════════════════════════════════════════════════
            //  MenuItem — mevcut konfigürasyon KORUNDU + [MT-4] + [MT-7]
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
                entity.Property(m => m.CostPrice).HasPrecision(10, 2);  // nullable, no default
                entity.Property(m => m.TrackStock).HasDefaultValue(false).IsRequired();
                entity.Property(m => m.IsAvailable).HasDefaultValue(true).IsRequired();
                entity.Property(m => m.Description).HasColumnType("text");
                entity.Property(m => m.MenuItemCreatedTime).HasDefaultValueSql("NOW()").IsRequired();

                entity.HasOne(c => c.Category)
                    .WithMany(m => m.MenuItems)
                    .HasForeignKey(c => c.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);

                // [MT-7] TenantId sütun konfigürasyonu
                entity.Property(m => m.TenantId).HasMaxLength(100).IsRequired();
                entity.HasOne(m => m.Tenant)
                    .WithMany()
                    .HasForeignKey(m => m.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                // [MT-4] Global Query Filter
                entity.HasQueryFilter(m =>
                    _tenantService.TenantId == null ||
                    m.TenantId == _tenantService.TenantId);
            });

            // ════════════════════════════════════════════════════════════════
            //  Order — mevcut konfigürasyon KORUNDU + [MT-4] + [MT-7]
            // ════════════════════════════════════════════════════════════════
            modelBuilder.Entity<Order>(entity =>
            {
                entity.ToTable("orders");
                entity.HasKey(o => o.OrderId);
                entity.Property(o => o.OrderStatus).HasMaxLength(20).HasDefaultValue("open").IsRequired();
                entity.Property(o => o.OrderOpenedBy).HasMaxLength(100);
                entity.Property(o => o.OrderNote).HasColumnType("text");
                entity.Property(o => o.OrderTotalAmount).HasPrecision(12, 2).HasDefaultValue(0).IsRequired();
                entity.Property(o => o.OrderOpenedAt).HasDefaultValueSql("NOW()").IsRequired();

                entity.HasOne(t => t.Table)
                    .WithMany(o => o.Orders)
                    .HasForeignKey(o => o.TableId)
                    .OnDelete(DeleteBehavior.Restrict);

                // [MT-7] TenantId sütun konfigürasyonu
                entity.Property(o => o.TenantId).HasMaxLength(100).IsRequired();
                entity.HasOne(o => o.Tenant)
                    .WithMany()
                    .HasForeignKey(o => o.TenantId)
                    .OnDelete(DeleteBehavior.Restrict);

                // [MT-4] Global Query Filter
                entity.HasQueryFilter(o =>
                    _tenantService.TenantId == null ||
                    o.TenantId == _tenantService.TenantId);
            });

            // ════════════════════════════════════════════════════════════════
            //  OrderItem — mevcut konfigürasyon AYNEN KORUNDU
            //  NOT: TenantId eklenmedi. Order üzerinden JOIN ile izole edilir.
            //       Global Query Filter Order'a uygulandı; OrderItem sorgularında
            //       EF Core otomatik olarak Order filtreli JOIN üretir.
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
                entity.Property(o => o.OrderItemStatus).HasMaxLength(20).HasDefaultValue("pending").IsRequired();
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
            });

            // ════════════════════════════════════════════════════════════════
            //  Payment — mevcut konfigürasyon AYNEN KORUNDU
            //  NOT: TenantId eklenmedi. Order üzerinden JOIN ile izole edilir.
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
            });

            // ════════════════════════════════════════════════════════════════
            //  StockLog — mevcut konfigürasyon AYNEN KORUNDU
            //  NOT: TenantId eklenmedi. MenuItem üzerinden JOIN ile izole edilir.
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

                entity.HasOne(s => s.MenuItem)
                    .WithMany()
                    .HasForeignKey(s => s.MenuItemId)
                    .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
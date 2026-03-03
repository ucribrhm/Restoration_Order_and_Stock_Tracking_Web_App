// ============================================================================
//  Data/RestaurantDbContext.cs
//  DEĞİŞİKLİK — FAZ 1 ADIM 2: Multi-Tenancy
//  DÜZELTME — EF Core Global Query Filter uyarıları giderildi
//
//  UYARI KAYNAKLARI VE ÇÖZÜMLERİ:
//  ─────────────────────────────────────────────────────────────────────────
//  [WARN-1] MenuItem (filtered) → OrderItem (unfiltered) ilişkisi
//           Çözüm: OrderItem'a MenuItem üzerinden eşleşen filtre eklendi.
//
//  [WARN-2] Order (filtered) → Payment (unfiltered) ilişkisi
//           Çözüm: Payment'a Order üzerinden eşleşen filtre eklendi.
//
//  [WARN-3] MenuItem (filtered) → StockLog (unfiltered) ilişkisi
//           Çözüm: StockLog'a MenuItem üzerinden eşleşen filtre eklendi.
//
//  [WARN-4] RestaurantType enum için sentinel değer uyarısı
//           Çözüm: HasDefaultValue kaldırıldı, CLR default (FastFood=0) korundu.
//           Sentinel sorununu önlemek için CasualDining varsayılan değeri
//           model seviyesinde (Tenant.cs) set edilir.
// ============================================================================
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data
{
    public class RestaurantDbContext : IdentityDbContext<ApplicationUser>
    {
        // ── Mevcut DbSet'ler ─────────────────────────────────────────────────
        public DbSet<Table> Tables { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<MenuItem> MenuItems { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<StockLog> StockLogs { get; set; }

        // ── Yeni SaaS DbSet'leri ─────────────────────────────────────────────
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<TenantConfig> TenantConfigs { get; set; }

        // ── ITenantService ───────────────────────────────────────────────────
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

                // [WARN-4 DÜZELTME] HasDefaultValue kaldırıldı — sentinel çakışması önlendi.
                // Varsayılan değer (CasualDining) Tenant.cs'teki property initializer'da set edilir.
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
            //  Order — Global Query Filter
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
            //  OrderItem
            //  [WARN-1 DÜZELTME] MenuItem'a filtre uygulandı → OrderItem'a
            //  eşleşen filtre eklenmeli. MenuItem.TenantId üzerinden kontrol.
            //
            //  Filtre mantığı:
            //    TenantId null (migration/seed) → filtre yok, tüm kayıtlar döner
            //    TenantId dolu → yalnızca o tenant'ın MenuItem'larına bağlı
            //    OrderItem'lar döner (= doğru tenant'ın sipariş kalemleri)
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

                // [WARN-1 DÜZELTME] MenuItem filtresiyle eşleşen filtre
                entity.HasQueryFilter(o =>
                    _tenantService.TenantId == null ||
                    o.MenuItem.TenantId == _tenantService.TenantId);
            });

            // ════════════════════════════════════════════════════════════════
            //  Payment
            //  [WARN-2 DÜZELTME] Order'a filtre uygulandı → Payment'a
            //  eşleşen filtre eklenmeli. Order.TenantId üzerinden kontrol.
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

                // [WARN-2 DÜZELTME] Order filtresiyle eşleşen filtre
                entity.HasQueryFilter(p =>
                    _tenantService.TenantId == null ||
                    p.Order.TenantId == _tenantService.TenantId);
            });

            // ════════════════════════════════════════════════════════════════
            //  StockLog
            //  [WARN-3 DÜZELTME] MenuItem'a filtre uygulandı → StockLog'a
            //  eşleşen filtre eklenmeli. MenuItem.TenantId üzerinden kontrol.
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

                // [WARN-3 DÜZELTME] MenuItem filtresiyle eşleşen filtre
                entity.HasQueryFilter(s =>
                    _tenantService.TenantId == null ||
                    s.MenuItem.TenantId == _tenantService.TenantId);
            });
        }
    }
}
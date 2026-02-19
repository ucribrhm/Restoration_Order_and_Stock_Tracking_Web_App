using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data
{
    public class RestaurantDbContext : DbContext
    {
        public DbSet<Table> Tables { get; set; }
        public DbSet<Category> Categories { get; set; }

        public DbSet<Order> Orders { get; set; }

        public DbSet<MenuItem> MenuItems { get; set; }

        public DbSet<Order> OrderItems { get; set; }

        public DbSet<Payment> Payments { get; set; }
        public RestaurantDbContext(DbContextOptions options) : base(options)
        {
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Table>(entity =>
            {
                entity.ToTable("tables");

                entity.HasKey(t => t.TableId);

                entity.Property(t => t.TableName)
                .HasMaxLength(50)
                .IsRequired();

                entity.Property(t => t.TableCapacity)
                .HasMaxLength(2)
                .HasDefaultValue(4)
                .IsRequired();

                entity.Property(t => t.TableStatus)
                .HasDefaultValue(0)
                .IsRequired();

                entity.Property(t => t.TableCreatedAt)
                .HasDefaultValueSql("NOW()") //PostgreSql NOW fonks
                .IsRequired();
            });

            modelBuilder.Entity<Category>(entity =>
            {
                entity.ToTable("categories");

                entity.HasKey(c => c.CategoryId);

                entity.Property(c => c.CategoryName)
                .HasMaxLength(100)
                .IsRequired();

                entity.Property(c => c.CategorySortOrder)
                .HasDefaultValue(0)
                .IsRequired();

                entity.Property(c => c.IsActive)
                .HasDefaultValue(true)
                .IsRequired();

                entity.HasIndex(c => c.CategoryName)
                .IsUnique()
                .HasDatabaseName("uq_categories_name");
                //IX_categories_Name
            });

            modelBuilder.Entity<MenuItem>(entity =>
            {
                entity.ToTable("menu_items");

                entity.HasKey(m => m.MenuItemId);

                entity.Property(m => m.MenuItemName)
                .HasMaxLength(200)
                .IsRequired();

                entity.Property(m => m.MenuItemPrice)
                .HasPrecision(10, 2)
                .IsRequired();

                entity.Property(m => m.StockQuantity)
                .HasDefaultValue(0)
                .IsRequired();

                entity.Property(m => m.TrackStock)
                .HasDefaultValue(false)
                .IsRequired();

                entity.Property(m => m.IsAvailable)
                .HasDefaultValue(true)
                .IsRequired();

                entity.Property(m => m.Description)
                .HasColumnType("text");

                entity.Property(m => m.MenuItemCreatedTime)
                .HasDefaultValueSql("NOW()")
                .IsRequired();

                entity.HasOne(c => c.Category)//Benim birim Category
                .WithMany(m => m.MenuItems) //Bir kategoride çok menü ürünüm var 
                .HasForeignKey(c => c.CategoryId)//bağlı id' ise categoryıd
                .OnDelete(DeleteBehavior.Restrict);  
            });

            modelBuilder.Entity<Order>(entity =>
            {
                entity.ToTable("orders");

                entity.HasKey(o => o.OrderId);

                entity.Property(o => o.OrderStatus)
                .HasMaxLength(20)
                .HasDefaultValue("open")
                .IsRequired();

                entity.Property(o => o.OrderOpenedBy)
                .HasMaxLength(100);

                entity.Property(o => o.OrderNote)
                .HasColumnType("text");

                entity.Property(o => o.OrderTotalAmount)
                .HasPrecision(12,2)
                .HasDefaultValue(0)
                .IsRequired();

                entity.Property(o => o.OrderOpenedAt)
                .HasDefaultValueSql("NOW()")
                .IsRequired();

                entity.HasOne(t => t.Table)//Benim bir masam var
                .WithMany(o => o.Orders)//Bir masanın birden fazla orderı var
                .HasForeignKey(o => o.TableId)//Buna bağlı id tableId
                .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<OrderItem>(entity =>
            {
                entity.ToTable("order_items");

                entity.HasKey(o => o.OrderItemId);

                entity.Property(o => o.OrderItemQuantity)
                .IsRequired();

                entity.Property(o => o.OrderItemUnitPrice)
                .HasPrecision(10, 2)
                .IsRequired();

                entity.Property(o => o.OrderItemLineTotal)
                .HasPrecision(12,2)
                .IsRequired();

                entity.Property(o => o.OrderItemNote)
                .HasColumnType("text");

                entity.Property(o => o.OrderItemStatus)
                .HasMaxLength(20)
                .HasDefaultValue("pending")
                .IsRequired();

                entity.Property(o => o.OrderItemAddedAt)
                .HasDefaultValueSql("NOW()")
                .IsRequired();

                entity.HasOne(o => o.Order)//bir siparişin
                .WithMany(o => o.OrderItems)//birden fazla sipariş iöçeriği olabilir
                .HasForeignKey(o =>o.OrderId)//bu bağlantıda orderID ile sağlaıır
                .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(o => o.MenuItem) // Her sipariş kaleminin (OrderItem) bağlı olduğu tek bir menü ürünü vardır.
                .WithMany() // Menü ürününden siparişlere listeleme yapılmaz (Performans için tek yönlü ilişki).
                .HasForeignKey(o => o.MenuItemId) // Veritabanındaki bağlantı MenuItemId kolonu üzerinden sağlanır.
                .OnDelete(DeleteBehavior.Restrict); // Geçmişte sipariş edilmiş bir ürünün menüden yanlışlıkla silinmesini engeller.

            });

            modelBuilder.Entity<Payment>(entity =>
            {
                entity.ToTable("payments");
                entity.HasKey(p => p.PaymentId);

                entity.Property(p => p.PaymentsMethod)
               .IsRequired()
               .HasDefaultValue(0);


                entity.Property(p => p.PaymentsAmount)
                .HasDefaultValue(0)
                .HasPrecision(10, 2)
                .IsRequired();

                entity.Property(p => p.PaymentsChangeGiven)
                .HasDefaultValue(0)
                .HasPrecision(10, 2)
                .IsRequired();


                entity.Property(p => p.PaymentsPaidAt)
               .HasDefaultValueSql("NOW()")
               .IsRequired();

                entity.Property(p => p.PaymentsNote)
                .IsRequired()
                .HasColumnType("text");

                entity.HasOne(o => o.Order)
                .WithMany(p => p.Payments)
                .HasForeignKey(o => o.OrderId)
                .OnDelete(DeleteBehavior.Restrict);
            });

        }

    }
}

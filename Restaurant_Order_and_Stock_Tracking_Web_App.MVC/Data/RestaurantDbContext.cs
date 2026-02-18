using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data
{
    public class RestaurantDbContext : DbContext
    {
        public DbSet<Table> Tables { get; set; }
        public DbSet<Category> Categories { get; set; }

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

                entity.HasKey(m => m.CategoryId);

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
        }  

    }
}

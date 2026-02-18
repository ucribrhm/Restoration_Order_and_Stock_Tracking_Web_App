using Microsoft.EntityFrameworkCore;
using Restoration_Order_and_Stock_Tracking_Web_App.MVC.Models;

namespace Restoration_Order_and_Stock_Tracking_Web_App.MVC.Data
{
    public class RestaurantDbContext : DbContext
    {
        public DbSet<Table> Tables { get; set; }

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
        }  

    }
}

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // ── QuestPDF Community License ───────────────────────────────
            QuestPDF.Settings.License = LicenseType.Community;

            var builder = WebApplication.CreateBuilder(args);

            // ── DbContext ────────────────────────────────────────────────
            builder.Services.AddDbContext<RestaurantDbContext>(options =>
                options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

            // ── Identity ─────────────────────────────────────────────────
            builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 6;
                options.Password.RequiredUniqueChars = 1;

                options.User.RequireUniqueEmail = false;
                options.SignIn.RequireConfirmedEmail = false;
                options.SignIn.RequireConfirmedAccount = false;

                options.Lockout.AllowedForNewUsers = false;
            })
            .AddEntityFrameworkStores<RestaurantDbContext>()
            .AddDefaultTokenProviders();

            // ── Cookie / Oturum Ayarları ─────────────────────────────────
            builder.Services.ConfigureApplicationCookie(options =>
            {
                options.LoginPath = "/Auth/Login";
                options.AccessDeniedPath = "/Auth/AccessDenied";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Name = "RestaurantOS.Auth";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
            });

            // ── Security Stamp Validation ────────────────────────────────
            builder.Services.Configure<SecurityStampValidatorOptions>(options =>
            {
                options.ValidationInterval = TimeSpan.FromMinutes(30); // 30sn → oturum düşürüyordu; stamp ÖNCE güncelleniyor artık
            });

            // ── Background Service ───────────────────────────────────────
            builder.Services.AddHostedService<ReservationCleanupService>();

            // ── MVC ──────────────────────────────────────────────────────
            builder.Services.AddControllersWithViews();
            // ── SignalR ──────────────────────────────────────────────────
            // YENİ: SignalR servisini DI container'a kaydet
            builder.Services.AddSignalR();


            var app = builder.Build();

            // ── Middleware Pipeline ──────────────────────────────────────
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseAuthentication();   // UseAuthorization'dan ÖNCE
            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
                .WithStaticAssets();
            // ── SignalR Hub Endpoint ─────────────────────────────────────
            // YENİ: Hub'ı "/hubs/restaurant" path'ine bağla
            app.MapHub<RestaurantHub>("/hubs/restaurant");

            // ── Rol Seed: Uygulama başlarken Admin/Garson/Kasiyer rollerini garantile ──
            // Rol yoksa oluşturur; varsa dokunmaz. Her deploy'da güvenle çalışır.
            using (var scope = app.Services.CreateScope())
            {
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

                // Rolleri garantile
                foreach (var roleName in new[] { "Admin", "Garson", "Kasiyer" })
                {
                    if (!await roleManager.RoleExistsAsync(roleName))
                        await roleManager.CreateAsync(new IdentityRole(roleName));
                }

                // İlk Admin: sistemde hiç Admin yoksa oluştur
                // Giriş: kullanıcı adı → admin  |  şifre → Admin123
                // Giriş yaptıktan sonra /User/Edit üzerinden şifreyi değiştirin!
                if ((await userManager.GetUsersInRoleAsync("Admin")).Count == 0)
                {
                    var adminUser = new ApplicationUser
                    {
                        UserName = "admin",
                        FullName = "Sistem Yöneticisi",
                        EmailConfirmed = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    var createResult = await userManager.CreateAsync(adminUser, "Admin123");
                    if (createResult.Succeeded)
                        await userManager.AddToRoleAsync(adminUser, "Admin");
                }
            }


            app.Run();
        }
    }
}
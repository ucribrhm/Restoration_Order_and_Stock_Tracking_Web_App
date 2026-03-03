// ════════════════════════════════════════════════════════════════════════════
//  Program.cs
//  Yol: Restaurant_Order_and_Stock_Tracking_Web_App.MVC/
//
//  FAZ 1 GÜVENLİK DEĞİŞİKLİKLERİ (3 blok — diğer her satır orijinalle aynı):
//  [GÜV-1] AddIdentity → Lockout + Password politikası sıkılaştırıldı.
//  [GÜV-2] ConfigureApplicationCookie → SecurePolicy Always, SameSite Strict.
//  [GÜV-3] Admin seed → şifre artık ADMIN_INITIAL_PASSWORD env-var'dan okunuyor;
//           değişken eksikse uygulama InvalidOperationException fırlatarak durur.
// ════════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Authentication;
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
            QuestPDF.Settings.License = LicenseType.Community;

            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddDbContext<RestaurantDbContext>(options =>
                options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

            // ════════════════════════════════════════════════════════════════
            //  [GÜV-1] Identity — Brute-Force Koruması & Şifre Politikası
            //
            //  ÖNCE (güvensiz):
            //    options.Lockout.AllowedForNewUsers = false;  ← kilitlenme kapalı!
            //    options.Password.RequireDigit     = false;   ← zayıf şifre
            //    options.Password.RequireUppercase = false;   ← zayıf şifre
            //    options.Password.RequiredLength   = 6;       ← çok kısa
            //
            //  SONRA (güvenli): Aşağıdaki değerler uygulandı.
            // ════════════════════════════════════════════════════════════════
            builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                // ── Şifre Politikası ─────────────────────────────────────
                options.Password.RequireDigit = true;   // [GÜV-1] rakam zorunlu
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;   // [GÜV-1] büyük harf zorunlu
                options.Password.RequireNonAlphanumeric = false;  // özel karakter opsiyonel
                options.Password.RequiredLength = 8;      // [GÜV-1] min 8 karakter
                options.Password.RequiredUniqueChars = 1;

                // ── Brute-Force Kilitleme ─────────────────────────────────
                options.Lockout.AllowedForNewUsers = true;                // [GÜV-1] kilitleme açık
                options.Lockout.MaxFailedAccessAttempts = 5;                 // [GÜV-1] 5 hatalı → kilit
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); // [GÜV-1] 15 dk kilit

                // ── Kullanıcı & Giriş Ayarları (değişmedi) ───────────────
                options.User.RequireUniqueEmail = false;
                options.SignIn.RequireConfirmedEmail = false;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<RestaurantDbContext>()
            .AddDefaultTokenProviders();

            // ════════════════════════════════════════════════════════════════
            //  [GÜV-2] Cookie Güvenliği
            //
            //  ÖNCE (güvensiz):
            //    SecurePolicy = CookieSecurePolicy.SameAsRequest  ← HTTP'de çerez açıkta!
            //    SameSite     = SameSiteMode.Lax                  ← CSRF riski
            //
            //  SONRA (güvenli): Always + Strict.
            // ════════════════════════════════════════════════════════════════
            builder.Services.ConfigureApplicationCookie(options =>
            {
                options.LoginPath = "/Auth/Login";
                options.AccessDeniedPath = "/Auth/AccessDenied";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;    // [GÜV-2] yalnızca HTTPS
                options.Cookie.SameSite = SameSiteMode.Strict;          // [GÜV-2] CSRF koruması
                options.Cookie.Name = "RestaurantOS.Auth";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
            });

            builder.Services.Configure<SecurityStampValidatorOptions>(options =>
            {
                options.ValidationInterval = TimeSpan.FromMinutes(30);
            });

            builder.Services.AddHostedService<ReservationCleanupService>();

            // ── [MT] Multi-Tenancy Servisleri ─────────────────────────────────────
            // IHttpContextAccessor: HttpContextTenantService için HTTP bağlamına erişim
            builder.Services.AddHttpContextAccessor();
            // ITenantService: Global Query Filter'ın TenantId'yi okuduğu servis
            builder.Services.AddScoped<ITenantService, HttpContextTenantService>();
            // IClaimsTransformation: Login sırasında TenantId'yi Claims'e yazar
            // Döngüsel bağımlılığı önleyen mekanizmanın kalbi
            builder.Services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();
            // ────────────────────────────────────────────────────────────────────────

            builder.Services.AddControllersWithViews();

            // ── SignalR (değişmedi) ──────────────────────────────────────
            builder.Services.AddSignalR();

            var app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapStaticAssets();
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}")
                .WithStaticAssets();

            // ── SignalR Hub Endpoints (değişmedi) ────────────────────────
            app.MapHub<RestaurantHub>("/hubs/restaurant");
            app.MapHub<NotificationHub>("/notificationHub");

            // ════════════════════════════════════════════════════════════════
            //  Rol & Admin Seed
            // ════════════════════════════════════════════════════════════════
            using (var scope = app.Services.CreateScope())
            {
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

                // ── Roller (değişmedi) ────────────────────────────────────
                foreach (var roleName in new[] { "Admin", "Garson", "Kasiyer" })
                {
                    if (!await roleManager.RoleExistsAsync(roleName))
                        await roleManager.CreateAsync(new IdentityRole(roleName));
                }

                // ── Admin Kullanıcı Seed ──────────────────────────────────
                //
                //  [GÜV-3] ÖNCE (tehlikeli):
                //    await userManager.CreateAsync(adminUser, "Admin123");
                //    → şifre kaynak koduna gömülü; git geçmişinde sonsuza dek görünür.
                //
                //  SONRA (güvenli):
                //    Şifre ADMIN_INITIAL_PASSWORD ortam değişkeninden okunur.
                //    Değişken tanımsızsa uygulama başlamaz — eksik konfigürasyon
                //    sessizce geçilmek yerine açıkça patlayarak bildirilir.
                //
                //  Ortam değişkenini ayarlama örnekleri:
                //    Linux/macOS  : export ADMIN_INITIAL_PASSWORD="İlkGüvenliŞifre2024!"
                //    Windows CMD  : set    ADMIN_INITIAL_PASSWORD=İlkGüvenliŞifre2024!
                //    Docker       : -e ADMIN_INITIAL_PASSWORD=İlkGüvenliŞifre2024!
                //    .env (docker-compose) : ADMIN_INITIAL_PASSWORD=İlkGüvenliŞifre2024!
                //    appsettings.Development.json (ASLA git'e commit edilmemeli!):
                //      { "ADMIN_INITIAL_PASSWORD": "İlkGüvenliŞifre2024!" }
                // ─────────────────────────────────────────────────────────
                if ((await userManager.GetUsersInRoleAsync("Admin")).Count == 0)
                {
                    // [GÜV-3] Şifre env-var'dan okunuyor; eksikse uygulama başlamıyor.
                    var adminPassword = builder.Configuration["ADMIN_INITIAL_PASSWORD"]
                        ?? throw new InvalidOperationException(
                            "Kritik Konfigürasyon Eksik: 'ADMIN_INITIAL_PASSWORD' ortam değişkeni " +
                            "tanımlanmamış. Uygulamayı başlatmadan önce bu değişkeni ayarlayın. " +
                            "Örnek: export ADMIN_INITIAL_PASSWORD=\"GüvenliŞifre2024!\"");

                    var adminUser = new ApplicationUser
                    {
                        UserName = "admin",
                        FullName = "Sistem Yöneticisi",
                        EmailConfirmed = true,
                        CreatedAt = DateTime.UtcNow
                    };

                    var createResult = await userManager.CreateAsync(adminUser, adminPassword);
                    if (createResult.Succeeded)
                        await userManager.AddToRoleAsync(adminUser, "Admin");
                }
            }

            app.Run();
        }
    }
}
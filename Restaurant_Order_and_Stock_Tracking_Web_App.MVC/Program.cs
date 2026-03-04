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

            // [MOD] Modüler Monolith — Sipariş servisi
            builder.Services.AddScoped<Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Modules.Orders.IOrderService,
                Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Modules.Orders.OrderService>();
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

            // ════════════════════════════════════════════════════════════════
            //  Rol, Tenant & Admin Seed
            //  [MT] Tenant olmadan FK kısıtı ihlal oluyor.
            //  Bu blok her başlangıçta idempotent çalışır (AnyAsync kontrolü).
            // ════════════════════════════════════════════════════════════════
            using (var scope = app.Services.CreateScope())
            {
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();

                // ── Roller ───────────────────────────────────────────────
                foreach (var roleName in new[] { "Admin", "Garson", "Kasiyer", "Kitchen" })
                {
                    if (!await roleManager.RoleExistsAsync(roleName))
                        await roleManager.CreateAsync(new IdentityRole(roleName));
                }

                // ── [MT] Varsayılan Tenant Seed ──────────────────────────
                //  FK_tables_tenants_TenantId hatasının kök sebebi:
                //  tenants tablosunda kayıt yok → masa eklenemez.
                //  Bu blok ilk çalışmada tenant'ı oluşturur, sonrakilerde atlar.
                const string defaultTenantId = "varsayilan-restoran";

                if (!await db.Tenants.AnyAsync(t => t.TenantId == defaultTenantId))
                {
                    db.Tenants.Add(new Tenant
                    {
                        TenantId = defaultTenantId,
                        Name = "Varsayılan Restoran",
                        Subdomain = "varsayilan",
                        PlanType = "trial",
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        TrialEndsAt = DateTime.UtcNow.AddDays(30),
                        RestaurantType = RestaurantType.CasualDining,
                        Config = new TenantConfig
                        {
                            TenantId = defaultTenantId,
                            EnableKitchenDisplay = true,
                            EnableReservations = true,
                            EnableDiscounts = true,
                            CurrencyCode = "TRY"
                        }
                    });
                    await db.SaveChangesAsync();
                }

                // ── Admin Kullanıcı Seed ─────────────────────────────────
                if ((await userManager.GetUsersInRoleAsync("Admin")).Count == 0)
                {
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
                        CreatedAt = DateTime.UtcNow,
                        TenantId = defaultTenantId   // [MT] FK için zorunlu
                    };

                    var createResult = await userManager.CreateAsync(adminUser, adminPassword);
                    if (createResult.Succeeded)
                        await userManager.AddToRoleAsync(adminUser, "Admin");
                }
                else
                {
                    // Mevcut admin'in TenantId'si null ise ata
                    // (eski DB'den geçiş — bu satır olmadan Claims boş kalır)
                    var admins = await userManager.GetUsersInRoleAsync("Admin");
                    foreach (var a in admins.Where(a => a.TenantId == null))
                    {
                        a.TenantId = defaultTenantId;
                        await userManager.UpdateAsync(a);
                    }
                }
            }

            app.Run();
        }
    }
}
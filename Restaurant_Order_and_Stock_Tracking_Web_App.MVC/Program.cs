// ════════════════════════════════════════════════════════════════════════════
//  Program.cs
//  Yol: Restaurant_Order_and_Stock_Tracking_Web_App.MVC/
//
//  FAZ 1 GÜVENLİK DEĞİŞİKLİKLERİ (korundu):
//  [GÜV-1] AddIdentity → Lockout + Password politikası sıkılaştırıldı.
//  [GÜV-3] Admin seed → şifre ADMIN_INITIAL_PASSWORD env-var'dan okunuyor.
//
//  AREAS SPRINT 1:
//  [AREAS-AUTH]   ConfigureApplicationCookie kaldırıldı.
//                 AddAuthentication() → AdminAuth + AppAuth çift cookie scheme.
//  [AREAS-ROUTES] Üç route tanımı: Admin → App → Default (sıralama kritik).
//
//  AREAS SPRINT 3:
//  [AREAS-SEED]   SysAdmin rolü ve sysadmin kullanıcısı seed bloğuna eklendi.
//
//  AREAS SPRINT 4:
//  [SPRINT-4]     ITenantOnboardingService kaydedildi.
//                 Default route controller=Landing olarak güncellendi.
// ════════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Hubs;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;
using System.Threading.RateLimiting;

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
            // ════════════════════════════════════════════════════════════════
            builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                // ── Şifre Politikası ─────────────────────────────────────
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredLength = 8;
                options.Password.RequiredUniqueChars = 1;

                // ── Brute-Force Kilitleme ─────────────────────────────────
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

                // ── Kullanıcı & Giriş Ayarları ────────────────────────────
                options.User.RequireUniqueEmail = false;
                options.SignIn.RequireConfirmedEmail = false;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<RestaurantDbContext>()
            .AddDefaultTokenProviders();

            // ════════════════════════════════════════════════════════════════
            //  [AREAS-AUTH] Çift Cookie Scheme — AdminAuth & AppAuth
            //
            //  ConfigureApplicationCookie kaldırıldı.
            //  Giriş işlemleri HttpContext.SignInAsync() ile elle yapılıyor.
            //  Bir Garson'un AppAuth cookie'si AdminAuth ile korunan
            //  endpoint'e teknik olarak izin VERMİYOR.
            // ════════════════════════════════════════════════════════════════
            builder.Services.AddAuthentication()
                .AddCookie("AdminAuth", options =>
                {
                    options.LoginPath = "/Admin/Auth/Login";
                    options.AccessDeniedPath = "/Admin/Auth/AccessDenied";
                    options.Cookie.Name = "RestaurantOS.AdminAuth";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    options.Cookie.SameSite = SameSiteMode.Strict;
                    options.ExpireTimeSpan = TimeSpan.FromHours(8);
                    options.SlidingExpiration = true;
                })
                .AddCookie("AppAuth", options =>
                {
                    options.LoginPath = "/App/Auth/Login";
                    options.AccessDeniedPath = "/App/Auth/AccessDenied";
                    options.Cookie.Name = "RestaurantOS.AppAuth";
                    options.Cookie.HttpOnly = true;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                    options.Cookie.SameSite = SameSiteMode.Strict;
                    options.ExpireTimeSpan = TimeSpan.FromHours(8);
                    options.SlidingExpiration = true;
                });

            builder.Services.Configure<SecurityStampValidatorOptions>(options =>
            {
                options.ValidationInterval = TimeSpan.FromMinutes(30);
            });

            builder.Services.AddHostedService<ReservationCleanupService>();

            // ── [MT] Multi-Tenancy Servisleri ─────────────────────────────
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ITenantService, HttpContextTenantService>();
            builder.Services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();

            // [SPRINT-4] Tenant Onboarding Servisi
            builder.Services.AddScoped<ITenantOnboardingService, TenantOnboardingService>();

            // [MOD] Modüler Monolith — Sipariş servisi
            builder.Services.AddScoped<Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Modules.Orders.IOrderService,
                Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Modules.Orders.OrderService>();

            builder.Services.AddControllersWithViews();

            // ── SignalR ──────────────────────────────────────────────────
            builder.Services.AddSignalR();

            // ════════════════════════════════════════════════════════════════
            //  [G-06] Rate Limiting — CallWaiter Spam Koruması
            // ════════════════════════════════════════════════════════════════
            builder.Services.AddRateLimiter(options =>
            {
                options.AddSlidingWindowLimiter(
                    policyName: "WaiterCallPolicy",
                    configureOptions: limiter =>
                    {
                        limiter.Window = TimeSpan.FromSeconds(60);
                        limiter.PermitLimit = 2;
                        limiter.SegmentsPerWindow = 6;
                        limiter.QueueLimit = 0;
                        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    });

                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.HttpContext.Response.ContentType = "application/json";
                    await context.HttpContext.Response.WriteAsync(
                        "{\"success\":false,\"message\":\"Çok sık istek gönderildi. Lütfen bekleyin.\"}",
                        cancellationToken);
                };
            });

            var app = builder.Build();

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Landing/Index");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseRouting();
            app.UseRateLimiter();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapStaticAssets();

            // ════════════════════════════════════════════════════════════════
            //  [AREAS-ROUTES] Route Tanımları — Sıralama Kritik!
            //
            //  Admin ve App route'ları default'tan ÖNCE tanımlanmalıdır.
            // ════════════════════════════════════════════════════════════════

            // [ROUTE-1] Admin Area
            app.MapControllerRoute(
                name: "Admin",
                pattern: "Admin/{controller=Home}/{action=Index}/{id?}",
                defaults: new { area = "Admin" });

            // [ROUTE-2] App Area
            app.MapControllerRoute(
                name: "App",
                pattern: "App/{controller=Tables}/{action=Index}/{id?}",
                defaults: new { area = "App" });

            // [ROUTE-3] Default Route — Landing & Onboarding
            // [SPRINT-4] controller=Landing — HomeController artık Areas/App içinde
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Landing}/{action=Index}/{id?}")
                .WithStaticAssets();

            // ── SignalR Hub Endpoints ────────────────────────────────────
            app.MapHub<RestaurantHub>("/hubs/restaurant");
            app.MapHub<NotificationHub>("/notificationHub");

            // ════════════════════════════════════════════════════════════════
            //  Rol, Tenant & Kullanıcı Seed
            //  Bu blok her başlangıçta idempotent çalışır (AnyAsync kontrolü).
            // ════════════════════════════════════════════════════════════════
            using (var scope = app.Services.CreateScope())
            {
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();

                // ── Roller ───────────────────────────────────────────────
                // [AREAS-SEED] SysAdmin rolü eklendi
                foreach (var roleName in new[] { "SysAdmin", "Admin", "Garson", "Kasiyer", "Kitchen" })
                {
                    if (!await roleManager.RoleExistsAsync(roleName))
                        await roleManager.CreateAsync(new IdentityRole(roleName));
                }

                // ── [MT] Varsayılan Tenant Seed ──────────────────────────
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
                        TenantId = defaultTenantId
                    };

                    var createResult = await userManager.CreateAsync(adminUser, adminPassword);
                    if (createResult.Succeeded)
                        await userManager.AddToRoleAsync(adminUser, "Admin");
                }
                else
                {
                    // Mevcut admin'in TenantId'si null ise ata (eski DB'den geçiş)
                    var admins = await userManager.GetUsersInRoleAsync("Admin");
                    foreach (var a in admins.Where(a => a.TenantId == null))
                    {
                        a.TenantId = defaultTenantId;
                        await userManager.UpdateAsync(a);
                    }
                }

                // ── [AREAS-SEED] SysAdmin Kullanıcı Seed ────────────────
                if ((await userManager.GetUsersInRoleAsync("SysAdmin")).Count == 0)
                {
                    var sysAdminPassword = builder.Configuration["SYSADMIN_INITIAL_PASSWORD"]
                        ?? throw new InvalidOperationException(
                            "Kritik Konfigürasyon Eksik: 'SYSADMIN_INITIAL_PASSWORD' ortam değişkeni " +
                            "tanımlanmamış. Uygulamayı başlatmadan önce bu değişkeni ayarlayın. " +
                            "Örnek: export SYSADMIN_INITIAL_PASSWORD=\"SaasAdmin2024!\"");

                    var sysAdmin = new ApplicationUser
                    {
                        UserName = "sysadmin",
                        FullName = "SaaS Sistem Yöneticisi",
                        EmailConfirmed = true,
                        CreatedAt = DateTime.UtcNow,
                        TenantId = null   // SysAdmin hiçbir tenant'a bağlı değil → Global Query Filter bypass
                    };

                    var createResult = await userManager.CreateAsync(sysAdmin, sysAdminPassword);
                    if (createResult.Succeeded)
                        await userManager.AddToRoleAsync(sysAdmin, "SysAdmin");
                }
            }

            app.Run();
        }
    }
}
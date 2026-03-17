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
//
//  SPRINT A — GÜVENLİK:
//  [SA-1] AppAuth OnRedirectToLogin event eklendi.
//         AJAX/fetch istekleri → 401 + JSON  (302 yerine).
//  [SA-2] TenantClaimsTransformation kaydı kaldırıldı.
//
//  SPRINT B — ALTYAPI:
//  [SB-1] AllowedUserNameCharacters açıkça tanımlandı.
//         Workspace Login formatı: "{tenantSlug}_{kısaAd}"
//         İzin verilen: a-z, A-Z, 0-9, tire (-), alt tire (_)
//         Kaldırılan: @ + . (e-posta stili karakterler)
//
//  SPRINT 1 — MİMARİ REFACTORING:
//  [S1-1] AddMemoryCache → DashboardService IMemoryCache için
//  [S1-2] IDashboardService, IStockService kayıtları
//
//  SPRINT 3 — GÜVENLİK:
//  [SEC-EX]      GlobalExceptionMiddleware → AJAX/View ayrımlı hata yanıtı
//  [SEC-HEADERS] X-Frame-Options, nosniff, Referrer-Policy, Permissions-Policy
//  [SEC-RL]      LoginPolicy, RegisterPolicy, QrMenuPolicy eklendi
//
//  SPRINT 4 — IMPERSONATION:
//  [IMP-2] ICurrentUserService → audit log kimlik servisi
//
//  SPRINT 5 — OTP E-POSTA DOĞRULAMA:
//  [OTP-8] IOtpService, IEmailSender, IEmailChannel, EmailBackgroundService
//  [OTP-8] OtpVerifyPolicy rate limiter (10 istek/dk)
//  appsettings.json'a eklenecek:
//    "Email": {
//      "SmtpHost": "smtp.gmail.com",  "SmtpPort": "587",
//      "SmtpUser": "GMAIL_ADRESINIZ", "SmtpPassword": "APP_PASSWORD",
//      "SenderAddress": "noreply@restaurantos.com", "SenderName": "RestaurantOS"
//    }
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

            // ── .env dosyasını oku ────────────────────────────────────────
            // TraversePath(): mevcut dizinden başlayıp üst klasörleri tarar.
            // Visual Studio'da çalışma dizini bin/Debug/net9.0 olduğundan
            // parametresiz Load() .env'i bulamaz — TraversePath zorunlu.
            // Production'da gerçek sistem env var'ları .env'i override eder.
            DotNetEnv.Env.TraversePath().Load();
            builder.Configuration.AddEnvironmentVariables();
            // ─────────────────────────────────────────────────────────────

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

                // ── [SB-1] Workspace Login Karakter İzni ─────────────────
                // Identity varsayılanı "@", "+" ve "." gibi e-posta karakterlerine
                // izin verir. Workspace Login formatımız "{tenantSlug}_{kısaAd}"
                // yalnızca harf, rakam, tire ve alt tire kullanır; fazlası yasak.
                options.User.AllowedUserNameCharacters =
                    "abcdefghijklmnopqrstuvwxyz" +
                    "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
                    "0123456789" +
                    "-_";   // tire: slug ayırıcısı  |  alt tire: tenant/kullanıcı ayırıcısı
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
            //
            //  [SA-1] AppAuth → OnRedirectToLogin event eklendi.
            //  AJAX/fetch istekleri (X-Requested-With: XMLHttpRequest veya
            //  Accept: application/json) için 302 yerine 401 + JSON döner.
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

                    // ── [SA-1] AJAX/Fetch 401 Handler ────────────────────
                    options.Events.OnRedirectToLogin = async ctx =>
                    {
                        var req = ctx.Request;
                        bool isAjax =
                            req.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                            (req.Headers["Accept"].ToString()
                                .Contains("application/json", StringComparison.OrdinalIgnoreCase));

                        if (isAjax)
                        {
                            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                            ctx.Response.ContentType = "application/json; charset=utf-8";
                            await ctx.Response.WriteAsync(
                                """{"error":"session_expired","redirectUrl":"/App/Auth/Login"}""");
                        }
                        else
                        {
                            ctx.Response.Redirect(ctx.RedirectUri);
                        }
                    };
                });

            builder.Services.Configure<SecurityStampValidatorOptions>(options =>
            {
                options.ValidationInterval = TimeSpan.FromMinutes(30);
            });

            builder.Services.AddHostedService<ReservationCleanupService>();

            // ── [MT] Multi-Tenancy Servisleri ─────────────────────────────
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddScoped<ITenantService, HttpContextTenantService>();

            // [SA-2] TenantClaimsTransformation KALDIRILDI.
            // App/AuthController.Login() zaten TenantId claim'ini elle ekliyor.
            // Silinen satır:
            //   builder.Services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();

            // [SPRINT-4] Tenant Onboarding Servisi
            builder.Services.AddScoped<ITenantOnboardingService, TenantOnboardingService>();

            // [MOD] Modüler Monolith — Sipariş servisi
            builder.Services.AddScoped<Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Modules.Orders.IOrderService,
                Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Modules.Orders.OrderService>();

            // ════════════════════════════════════════════════════════════════
            //  [S1-1/S1-2] Sprint 1 Refactoring — Servis Katmanı
            // ════════════════════════════════════════════════════════════════
            builder.Services.AddMemoryCache();
            builder.Services.AddScoped<IDashboardService, DashboardService>();
            builder.Services.AddScoped<IStockService, StockService>();

            // ── Tenant Feature Flag Servisi ────────────────────────────────
            // Scoped: her HTTP isteğinde taze DbContext ile çalışır.
            // IMemoryCache (AddMemoryCache yukarıda) üzerinde 5dk TTL ile cache'ler.
            // Cache key: tenant_features:{tenantId}
            builder.Services.AddScoped<
                Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services.ITenantFeatureService,
                Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services.TenantFeatureService>();

            // ── [IMP-2] Sprint 4 — Impersonation Audit Log Kimlik Servisi ──
            builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

            // ── [OTP-8] Sprint 5 — OTP E-posta Doğrulama ─────────────────
            builder.Services.AddScoped<IOtpService, OtpService>();
            builder.Services.AddSingleton<IEmailChannel, EmailChannel>();
            builder.Services.AddScoped<IEmailSender, GmailEmailSender>();
            builder.Services.AddHostedService<EmailBackgroundService>();

            builder.Services.AddControllersWithViews();

            // ── SignalR ──────────────────────────────────────────────────
            builder.Services.AddSignalR();

            // ════════════════════════════════════════════════════════════════
            //  [G-06] Rate Limiting
            //
            //  PolicyName       Algoritma      Limit     Pencere
            //  WaiterCallPolicy SlidingWindow  2 istek   60 sn
            //  LoginPolicy      FixedWindow    10 istek  60 sn
            //  RegisterPolicy   FixedWindow    3 istek   60 sn
            //  QrMenuPolicy     SlidingWindow  30 istek  60 sn
            // ════════════════════════════════════════════════════════════════
            builder.Services.AddRateLimiter(options =>
            {
                // ── Mevcut: Garson Çağırma — QR Menüden spam koruması ─────
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

                // ── [SEC-RL-1] Login Brute-Force Koruması ─────────────────
                options.AddFixedWindowLimiter(
                    policyName: "LoginPolicy",
                    configureOptions: limiter =>
                    {
                        limiter.Window = TimeSpan.FromSeconds(60);
                        limiter.PermitLimit = 10;
                        limiter.QueueLimit = 0;
                        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    });

                // ── [SEC-RL-2] Tenant Kayıt Spam Koruması ─────────────────
                options.AddFixedWindowLimiter(
                    policyName: "RegisterPolicy",
                    configureOptions: limiter =>
                    {
                        limiter.Window = TimeSpan.FromSeconds(60);
                        limiter.PermitLimit = 3;
                        limiter.QueueLimit = 0;
                        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    });

                // ── [SEC-RL-3] QR Menü Okuma Koruması ─────────────────────
                options.AddSlidingWindowLimiter(
                    policyName: "QrMenuPolicy",
                    configureOptions: limiter =>
                    {
                        limiter.Window = TimeSpan.FromSeconds(60);
                        limiter.PermitLimit = 30;
                        limiter.SegmentsPerWindow = 6;
                        limiter.QueueLimit = 0;
                        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    });

                // ── [OTP-8] OTP Doğrulama Koruması ────────────────────────
                options.AddFixedWindowLimiter(
                    policyName: "OtpVerifyPolicy",
                    configureOptions: limiter =>
                    {
                        limiter.Window = TimeSpan.FromSeconds(60);
                        limiter.PermitLimit = 10;
                        limiter.QueueLimit = 0;
                        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    });

                // ── [FP-1] Şifremi Unuttum Spam Koruması ──────────────────
                // IP başına 3 istek/60sn — SMTP spam + enumeration engeli
                // Tenant cooldown 2. katman olarak IOtpService içinde çalışır.
                options.AddFixedWindowLimiter(
                    policyName: "ForgotPasswordPolicy",
                    configureOptions: limiter =>
                    {
                        limiter.Window = TimeSpan.FromSeconds(60);
                        limiter.PermitLimit = 3;
                        limiter.QueueLimit = 0;
                        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    });

                // ── Merkezi 429 Yanıtı ────────────────────────────────────
                options.OnRejected = async (context, cancellationToken) =>
                {
                    var ctx = context.HttpContext;
                    ctx.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    ctx.Response.Headers["Retry-After"] = "60";

                    bool isAjax =
                        ctx.Request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
                        ctx.Request.Headers["Accept"]
                            .ToString()
                            .Contains("application/json", StringComparison.OrdinalIgnoreCase);

                    if (isAjax)
                    {
                        ctx.Response.ContentType = "application/json; charset=utf-8";
                        await ctx.Response.WriteAsync(
                            "{\"success\":false,\"message\":\"Çok fazla istek gönderdiniz. Lütfen biraz bekleyin.\"}",
                            cancellationToken);
                    }
                    else
                    {
                        ctx.Response.ContentType = "text/plain; charset=utf-8";
                        await ctx.Response.WriteAsync(
                            "Çok fazla istek gönderdiniz. Lütfen biraz bekleyin.",
                            cancellationToken);
                    }
                };
            });

            var app = builder.Build();

            // ════════════════════════════════════════════════════════════════
            //  [SEC-EX] Sprint 3 — Global Exception Handling
            //
            //  ESKİ (kaldırıldı):
            //    app.UseExceptionHandler("/Landing/Index")  ← sadece prod,
            //    AJAX'ta HTML 500 dönüyor, exception loglanmıyor.
            //
            //  YENİ:
            //    GlobalExceptionMiddleware → AJAX → JSON, View → /Error
            //    Her exception ILogger.LogError ile structured log.
            //    UseStatusCodePagesWithReExecute → 404/403 → ErrorController
            // ════════════════════════════════════════════════════════════════
            app.UseMiddleware<Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Middlewares.GlobalExceptionMiddleware>();
            app.UseStatusCodePagesWithReExecute("/Error/{0}");

            if (!app.Environment.IsDevelopment())
                app.UseHsts();

            app.UseHttpsRedirection();

            // ════════════════════════════════════════════════════════════════
            //  [SEC-HEADERS] Sprint 3 — Güvenlik Başlıkları
            //  UseHttpsRedirection'dan sonra, UseRouting'den önce.
            // ════════════════════════════════════════════════════════════════
            app.Use(async (ctx, next) =>
            {
                var h = ctx.Response.Headers;

                // Clickjacking: iframe gömme engeli
                h["X-Frame-Options"] = "DENY";

                // MIME sniffing: tarayıcı Content-Type'a uysun
                h["X-Content-Type-Options"] = "nosniff";

                // Referrer: dış sitelere sadece origin gönder
                h["Referrer-Policy"] = "strict-origin-when-cross-origin";

                // Permissions: kamera/mikrofon/konum kapat
                h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

                // XSS Protection: legacy tarayıcılar için
                h["X-XSS-Protection"] = "1; mode=block";

                // Sunucu kimliğini gizle
                h.Remove("Server");
                h.Remove("X-Powered-By");

                await next();
            });

            app.UseRouting();
            app.UseRateLimiter();
            app.UseAuthentication();
            app.UseAuthorization();

            app.MapStaticAssets();

            // ════════════════════════════════════════════════════════════════
            //  [AREAS-ROUTES] Route Tanımları — Sıralama Kritik!
            //
            //  [FIX-405] Landing route, Admin route'undan ÖNCE tanımlanmalıdır.
            // ════════════════════════════════════════════════════════════════

            // [ROUTE-0] Landing — Public, AllowAnonymous, area yok
            app.MapControllerRoute(
                name: "landing",
                pattern: "Landing/{action=Index}",
                defaults: new { controller = "Landing", area = "" });

            app.MapControllerRoute(
                name: "Admin",
                pattern: "Admin/{controller=Home}/{action=Index}/{id?}",
                defaults: new { area = "Admin" });

            app.MapControllerRoute(
                name: "App",
                pattern: "App/{controller=Tables}/{action=Index}/{id?}",
                defaults: new { area = "App" });

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
            // ════════════════════════════════════════════════════════════════
            using (var scope = app.Services.CreateScope())
            {
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                var db = scope.ServiceProvider.GetRequiredService<RestaurantDbContext>();

                foreach (var roleName in new[] { "SysAdmin", "Admin", "Garson", "Kasiyer", "Kitchen" })
                {
                    if (!await roleManager.RoleExistsAsync(roleName))
                        await roleManager.CreateAsync(new IdentityRole(roleName));
                }

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
                    var admins = await userManager.GetUsersInRoleAsync("Admin");
                    foreach (var a in admins.Where(a => a.TenantId == null))
                    {
                        a.TenantId = defaultTenantId;
                        await userManager.UpdateAsync(a);
                    }
                }

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
                        TenantId = null
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
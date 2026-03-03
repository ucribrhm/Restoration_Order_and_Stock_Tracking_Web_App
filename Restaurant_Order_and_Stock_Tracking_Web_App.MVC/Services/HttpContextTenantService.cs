// ============================================================================
//  Services/HttpContextTenantService.cs
//  SaaS Çok Kiracılı Yapı — HTTP Bağlamından Kiracı Çözümleyici
//
//  ┌─────────────────────────────────────────────────────────────────────┐
//  │  MİMARİ NOT — Döngüsel Bağımlılık Problemi ve Çözümü               │
//  │                                                                     │
//  │  Naif uygulama şöyle görünürdü:                                     │
//  │    ITenantService → UserManager<ApplicationUser>                    │
//  │                         ↓                                           │
//  │    UserManager → RestaurantDbContext                                │
//  │                         ↓                                           │
//  │    RestaurantDbContext → ITenantService   ← SONSUZ DÖNGÜ!           │
//  │                                                                     │
//  │  ÇÖZÜM: TenantClaimsTransformation                                  │
//  │  ─────────────────────────────────────────────────────────────────  │
//  │  1. Kullanıcı login olduğunda IClaimsTransformation devreye girer.  │
//  │  2. TenantClaimsTransformation, DB'den TenantId'yi okur ve          │
//  │     kullanıcının Claims koleksiyonuna "TenantId" Claim'i ekler.     │
//  │  3. Bu Claim şifrelenmiş cookie'ye yazılır.                         │
//  │  4. Sonraki TÜM isteklerde HttpContextTenantService YALNIZCA        │
//  │     bu Claim'i okur — DB'ye hiç dokunmaz.                           │
//  │                                                                     │
//  │  Sonuç: Döngüsel bağımlılık yok. Her istek için 0 DB sorgusu.      │
//  └─────────────────────────────────────────────────────────────────────┘
// ============================================================================
using System.Security.Claims;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services
{
    public class HttpContextTenantService : ITenantService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public HttpContextTenantService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// Kimliği doğrulanmış kullanıcının "TenantId" Claim değerini döner.
        /// TenantClaimsTransformation tarafından login sırasında eklenen Claim.
        ///
        /// null döner:
        ///   - Kullanıcı giriş yapmamış (anonim istek)
        ///   - Sistem bağlamı (seed, migration)
        ///   - Süper admin (TenantId Claim'i kasıtlı eklenmemiş)
        /// </summary>
        public string? TenantId
        {
            get
            {
                var user = _httpContextAccessor.HttpContext?.User;

                // Kullanıcı giriş yapmamışsa null döner
                if (user?.Identity?.IsAuthenticated != true)
                    return null;

                // "TenantId" Claim'ini oku.
                // Bu Claim TenantClaimsTransformation tarafından login sırasında eklendi.
                return user.FindFirstValue("TenantId");
            }
        }
    }
}
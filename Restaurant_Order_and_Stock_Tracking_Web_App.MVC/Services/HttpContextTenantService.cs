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
//
//  [AREAS-AUTH] Çift Scheme ile değişen davranış:
//
//  AppAuth  ile giriş yapan kullanıcı (Garson, Kasiyer, Admin):
//    → Cookie'de "TenantId" Claim'i VARDIR.
//    → Bu değer döner → Global Query Filter aktif → sadece kendi tenant'ı.
//
//  AdminAuth ile giriş yapan kullanıcı (SysAdmin):
//    → Cookie'de "TenantId" Claim'i YOKTUR (kasıtlı eklenmez).
//    → null döner → Global Query Filter bypass → tüm tenant'lar görünür.
//
//  Bu null davranışı bir hata değil; SaaS yöneticisinin tüm tenant
//  verilerine erişebilmesi için tasarımsal bir karardır.
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
        ///
        /// null döner:
        ///   - Kullanıcı giriş yapmamış (anonim istek)
        ///   - Sistem bağlamı (seed, migration)
        ///   - SysAdmin — AdminAuth cookie'sinde "TenantId" Claim'i kasıtlı yoktur,
        ///     bu nedenle Global Query Filter bypass olur ve tüm tenant verileri görünür.
        /// </summary>
        public string? TenantId
        {
            get
            {
                var user = _httpContextAccessor.HttpContext?.User;

                // Kullanıcı giriş yapmamışsa null döner.
                // (anonim istek, migration çalıştırma, seed işlemi)
                if (user?.Identity?.IsAuthenticated != true)
                    return null;

                // [AREAS-AUTH] Scheme kontrolü YAPILMIYOR — kasıtlı.
                //
                // Neden? Bu servis hangi scheme'in kullanıldığını bilmek
                // zorunda değil. Sadece "TenantId" Claim'inin varlığına bakıyor:
                //
                //   AppAuth  cookie'si → TenantId Claim var  → değeri döner
                //   AdminAuth cookie'si → TenantId Claim yok → null döner
                //
                // Böylece servis scheme-agnostik kalır ve her iki akış için
                // doğru davranışı otomatik üretir.
                return user.FindFirstValue("TenantId");
            }
        }
    }
}
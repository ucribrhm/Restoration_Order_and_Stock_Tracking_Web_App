// ============================================================================
//  Services/ITenantService.cs
//  SaaS Çok Kiracılı Yapı — Kiracı Bağlam Çözümleyici Arayüzü
//
//  Bu arayüz RestaurantDbContext tarafından EF Core Global Query Filter'da
//  kullanılır. O an işlem yapan HTTP isteğinin hangi kiracıya ait olduğunu
//  döner.
//
//  null dönme senaryoları (kasıtlı, EF migration uyumluluğu için kritik):
//    - Anonim istek (kullanıcı giriş yapmamış)
//    - EF Core migration çalıştırma (design-time, HTTP bağlamı yok)
//    - Uygulama başlangıcındaki seed işlemleri
//
//  null döndüğünde Global Query Filter devre dışı kalır.
//  Bu kasıtlı bir tasarım kararıdır; migration'ların tüm tenant verilerini
//  görebilmesi gerekir. Controller/Action katmanında yetki kontrolü ayrıca
//  yapılmaya devam eder.
// ============================================================================
namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services
{
    public interface ITenantService
    {
        /// <summary>
        /// O anki HTTP isteğinde kimliği doğrulanmış kullanıcının TenantId'si.
        /// Kullanıcı giriş yapmamışsa, sistem bağlamında veya migration
        /// çalıştırılırken null döner.
        /// </summary>
        string? TenantId { get; }
    }
}
// ============================================================================
//  Services/ITenantOnboardingService.cs
//  SaaS Onboarding — Yeni Restoran Kayıt Arayüzü
//
//  CreateTenantAsync:
//    → Tenant + TenantConfig + Admin kullanıcısını tek transaction'da oluşturur.
//    → Başarıda: (true, tenantId, null)
//    → Hata'da:  (false, null, hata mesajı)
// ============================================================================

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;

public record TenantOnboardingDto(
    string RestaurantName,
    string Subdomain,
    string AdminUsername,
    string Password,
    string FullName,
    string? Email
);

public interface ITenantOnboardingService
{
    Task<(bool Success, string? TenantId, string? Error)> CreateTenantAsync(TenantOnboardingDto dto);
}
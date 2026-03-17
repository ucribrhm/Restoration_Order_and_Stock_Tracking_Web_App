// ============================================================================
//  Features/ITenantFeatureService.cs
//
//  Tenant özellik bayraklarını okumak ve cache'i yönetmek için arayüz.
//  Uygulama: TenantFeatureService (IMemoryCache + lazy loading)
// ============================================================================

using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Dtos.Features;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services
{
    public interface ITenantFeatureService
    {
        /// <summary>
        /// Tenant'ın tüm özellik bayraklarını döndürür.
        /// Cache'de varsa cache'den okur; yoksa DB'den çekip cache'e yazar (lazy loading).
        /// Tenant veya config bulunamazsa <see cref="TenantFeaturesDto.Default"/> döner.
        /// </summary>
        /// <param name="tenantId">Tenant slug kimliği. Boş/null ise Default döner.</param>
        Task<TenantFeaturesDto> GetFeaturesAsync(string? tenantId);

        /// <summary>
        /// Belirtilen özelliğin tenant için aktif olup olmadığını döndürür.
        /// Özellik adı için <see cref="Features"/> sabit sınıfını kullanın.
        /// </summary>
        /// <param name="tenantId">Tenant slug kimliği.</param>
        /// <param name="featureName"><see cref="Features"/> sabitlerinden biri. Örn: Features.KDS</param>
        Task<bool> IsEnabledAsync(string? tenantId, string featureName);

        /// <summary>
        /// Tenant'ın feature cache'ini geçersiz kılar.
        /// TenantConfig güncellendiğinde (plan upgrade, özellik açma/kapama) çağrılmalı.
        /// </summary>
        /// <param name="tenantId">Cache'i temizlenecek tenant kimliği.</param>
        void InvalidateCache(string? tenantId);
    }
}
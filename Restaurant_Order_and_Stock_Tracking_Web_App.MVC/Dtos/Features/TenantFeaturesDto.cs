// ============================================================================
//  Features/TenantFeaturesDto.cs
//
//  Tenant'ın plan tipi ve aktif özelliklerini taşıyan immutable veri nesnesi.
//  IMemoryCache'de `tenant_features:{tenantId}` key'iyle saklanır.
//  TenantConfig tablosundan bir kez okunur, 5 dakika TTL ile cache'de tutulur.
//
//  KULLANIM:
//    var features = await _featureService.GetFeaturesAsync(tenantId);
//    if (features.EnableKDS) { ... }
// ============================================================================

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Dtos.Features
{
    /// <summary>
    /// Tenant'ın abonelik planı ve aktif özellik bayraklarını taşır.
    /// TenantConfig tablosundan okunup DTO'ya dönüştürülerek cache'lenir.
    /// </summary>
    public sealed record TenantFeaturesDto(
        // ── Abonelik Planı ──────────────────────────────────────────────────
        /// <summary>"trial" | "starter" | "pro" | "enterprise"</summary>
        string PlanType,

        // ── Operasyonel Özellik Bayrakları ──────────────────────────────────
        /// <summary>Mutfak Ekran Sistemi (KDS) — Kitchen Display System.</summary>
        bool EnableKDS

        /// <summary>Misafir bazlı hesap bölüşümü.</summary>
        
    )
    {
        /// <summary>
        /// Tenant veya config bulunamadığında kullanılan güvenli varsayılan.
        /// Tüm özellikler kapalı — erişim kısıtlaması olmaz ama özellikler gizlenir.
        /// </summary>
        public static readonly TenantFeaturesDto Default = new(
            PlanType: "trial",
            EnableKDS: false
           
        );
    }
}
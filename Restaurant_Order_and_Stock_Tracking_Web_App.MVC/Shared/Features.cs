// ============================================================================
//  Features/Features.cs
//
//  Tip güvenli (typo-safe) özellik adı sabitleri.
//  String literal kullanmak yerine bu sabitler kullanılarak hem IDE
//  auto-complete desteği hem de derleme zamanı hata yakalama sağlanır.
//
//  KULLANIM:
//    bool enabled = await _featureService.IsEnabledAsync(tenantId, Features.KDS);
//
//    // View tarafında da kullanılabilir:
//    @if (await FeatureService.IsEnabledAsync(tenantId, Features.SplitBill)) { ... }
// ============================================================================

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared
{
    /// <summary>
    /// Sistemdeki tüm özellik (feature flag) adlarının tip güvenli sabitleri.
    /// <see cref="ITenantFeatureService.IsEnabledAsync"/> ile birlikte kullanılır.
    /// </summary>
    public static class Features
    {
        // ── Operasyonel Özellikler ───────────────────────────────────────────

        /// <summary>Mutfak Ekran Sistemi (KDS). TenantConfig.EnableKitchenDisplay</summary>
        public const string KDS = "KitchenDisplay";

      

    }
}
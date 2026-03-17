// ============================================================================
//  Services/TenantFeatureService.cs
//
//  PLAN MATRİSİ (İş Kuralı):
//  KDS: pro=true | trial+aktif=true | starter/trial-bitti=false
//  Diğer tüm özellikler (SplitBill, TableMerge, QR, Rezerv, İndirim): her zaman true
// ============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Dtos.Features;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services
{
    public sealed class TenantFeatureService : ITenantFeatureService
    {
        private readonly RestaurantDbContext _db;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TenantFeatureService> _logger;

        private static string CacheKey(string tenantId) => $"tenant_features:{tenantId}";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

        public TenantFeatureService(
            RestaurantDbContext db,
            IMemoryCache cache,
            ILogger<TenantFeatureService> logger)
        {
            _db = db; _cache = cache; _logger = logger;
        }

        public async Task<TenantFeaturesDto> GetFeaturesAsync(string? tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId))
                return TenantFeaturesDto.Default;

            var key = CacheKey(tenantId);
            if (_cache.TryGetValue(key, out TenantFeaturesDto? cached) && cached is not null)
                return cached;

            _logger.LogDebug("[TenantFeatureService] Cache miss. TenantId: {TenantId}", tenantId);

            // ── ÖNEMLİ: && t.IsActive KASITLI OLARAK KALDIRILDI ──────────────
            // IsActive kontrolü AppBaseController Hard-Lock'ta yapılıyor.
            // Burada IsActive filtresi olursa IsActive=false tenant'lar Default
            // döndürür → EnableKDS=false → pro/trial kullanıcılar bile Upgrade'e gider.
            // TenantFeatureService sadece planType'a göre yetki hesaplar.
            var tenant = await _db.Tenants
                .AsNoTracking()
                .Select(t => new { t.TenantId, t.PlanType, t.TrialEndsAt })
                .FirstOrDefaultAsync(t => t.TenantId == tenantId);

            if (tenant is null)
            {
                _logger.LogWarning("[TenantFeatureService] Tenant bulunamadı: {TenantId}", tenantId);
                return TenantFeaturesDto.Default;
            }

            var dto = MapToDto(tenant.PlanType, tenant.TrialEndsAt);

            _cache.Set(key, dto, new MemoryCacheEntryOptions
            {
                AbsoluteExpiration = DateTimeOffset.UtcNow.Add(CacheTtl)
            });

            _logger.LogDebug(
                "[TenantFeatureService] Cache yazıldı. TenantId: {TenantId} | Plan: {Plan} | KDS: {Kds}",
                tenantId, dto.PlanType, dto.EnableKDS);

            return dto;
        }

        public async Task<bool> IsEnabledAsync(string? tenantId, string featureName)
        {
            var features = await GetFeaturesAsync(tenantId);
            return featureName switch
            {
                Features.KDS => features.EnableKDS,
                // Aşağıdakiler tüm planlarda ücretsiz — her zaman true
              
                _ => LogAndReturnFalse(tenantId, featureName)
            };
        }

        public void InvalidateCache(string? tenantId)
        {
            if (string.IsNullOrWhiteSpace(tenantId)) return;
            _cache.Remove(CacheKey(tenantId));
            _logger.LogInformation("[TenantFeatureService] Cache temizlendi. TenantId: {TenantId}", tenantId);
        }

        // ── MapToDto — İş Kuralı Merkezi ─────────────────────────────────────
        // DB'deki EnableKDS / EnableSplitBill vb. bool kolonları artık okunmaz.
        // Yetki tamamen plan tipine göre hesaplanır.
        //
        // KDS (tek premium özellik):
        //   "pro"   → true
        //   "trial" + TrialEndsAt > şimdi → true   (aktif trial)
        //   diğer   → false   (starter, süresi dolmuş trial)
        //
        // Diğer 5 özellik: tüm planlarda ücretsiz → sabit true
        private static TenantFeaturesDto MapToDto(string planType, DateTime? trialEndsAt)
        {
            var plan = (planType ?? "starter").ToLowerInvariant().Trim();

            bool kdsEnabled = plan switch
            {
                "pro" => true,
                "trial" => trialEndsAt.HasValue && trialEndsAt.Value > DateTime.UtcNow,
                _ => false   // starter veya bilinmeyen
            };

            return new TenantFeaturesDto(
                PlanType: plan,
                EnableKDS: kdsEnabled
               
            );
        }

        private bool LogAndReturnFalse(string? tenantId, string featureName)
        {
            _logger.LogWarning(
                "[TenantFeatureService] Bilinmeyen feature: '{Feature}'. TenantId: {TenantId}",
                featureName, tenantId);
            return false;
        }
    }
}
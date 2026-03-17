// ============================================================================
//  Filters/RequireFeatureAttribute.cs
//
//  Tenant özellik bayrağı (Feature Flag) tabanlı endpoint koruması.
//
//  KULLANIM — Controller veya Action üzerine:
//    [RequireFeature(Features.KDS)]
//    public IActionResult KitchenDisplay() { ... }
//
//    [RequireFeature(Features.SplitBill)]
//    public async Task<IActionResult> SplitBill(int orderId) { ... }
//
//  YANIT STRATEJİSİ:
//    ┌─ AJAX / JSON isteği  → HTTP 403 + JSON  { success, message, requiredPlan }
//    └─ Normal sayfa isteği → Redirect → /App/Subscription/Upgrade
//
//  BYPASS KURALI:
//    • IsImpersonation == "true"  → tüm kontroller atlanır (SysAdmin görevi)
//    • ClaimTypes.Role == SysAdmin → tüm kontroller atlanır
//
//  DI NOTU:
//    Attribute constructor'ında DI çalışmaz. Servis her çağrıda
//    context.HttpContext.RequestServices üzerinden çözümlenir.
// ============================================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;
using System.Security.Claims;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Filters
{
    /// <summary>
    /// Belirtilen <see cref="Features"/> özelliği tenant için aktif değilse
    /// isteği reddeden veya Upgrade sayfasına yönlendiren Action Filter.
    /// </summary>
    [AttributeUsage(
        AttributeTargets.Class | AttributeTargets.Method,
        AllowMultiple = true,   // Aynı action'a birden fazla özellik kısıtlaması uygulanabilir
        Inherited = true)]      // Base controller'dan türeyen sınıflara miras geçer
    public sealed class RequireFeatureAttribute : Attribute, IAsyncActionFilter
    {
        // ── Constructor ──────────────────────────────────────────────────────
        private readonly string _featureName;

        /// <param name="featureName">
        /// Kontrol edilecek özellik adı.
        /// Yazım hatalarını önlemek için <see cref="Features"/> sabitlerini kullanın.
        /// Örn: <c>Features.KDS</c>, <c>Features.SplitBill</c>
        /// </param>
        /// <exception cref="ArgumentNullException">featureName null veya boş olamaz.</exception>
        public RequireFeatureAttribute(string featureName)
        {
            if (string.IsNullOrWhiteSpace(featureName))
                throw new ArgumentNullException(nameof(featureName),
                    "RequireFeatureAttribute: featureName boş olamaz. Features.* sabitlerini kullanın.");

            _featureName = featureName;
        }

        // ── IAsyncActionFilter ───────────────────────────────────────────────
        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            var httpContext = context.HttpContext;
            var user = httpContext.User;

            // ── BYPASS 1: Impersonation ──────────────────────────────────────
            // SysAdmin, impersonation ile girmiş olabilir.
            // Bu durumda tüm özellik kısıtlamaları atlanır.
            var isImpersonation = user.FindFirstValue("IsImpersonation") == "true";
            if (isImpersonation)
            {
                await next();
                return;
            }

            // ── BYPASS 2: SysAdmin Rolü ─────────────────────────────────────
            // SysAdmin kendi oturumundan girmiş olabilir (ör: Admin paneli test).
            var isSysAdmin = user.IsInRole("SysAdmin");
            if (isSysAdmin)
            {
                await next();
                return;
            }

            // ── TenantId Çözümleme ───────────────────────────────────────────
            // Önce Claims'ten oku (normal login durumu).
            // KitchenController [AllowAnonymous] olduğu için Claims boş olabilir —
            // bu durumda "ros-tenant" cookie'sinden fallback yap.
            var tenantId = user.FindFirstValue("TenantId");

            if (string.IsNullOrWhiteSpace(tenantId))
                tenantId = httpContext.Request.Cookies["ros-tenant"];

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                SetDeniedResult(context, isAjax: IsAjaxRequest(httpContext),
                    reason: "Tenant kimliği bulunamadı. Lütfen tekrar giriş yapın.");
                return;
            }

            // ── Servis Çözümleme ─────────────────────────────────────────────
            // Attribute constructor'ında DI çalışmaz; RequestServices kullanılır.
            // GetRequiredService: servis kayıtlı değilse anlamlı hata fırlatır.
            var featureService = httpContext.RequestServices
                .GetRequiredService<ITenantFeatureService>();

            // ── Özellik Kontrolü ─────────────────────────────────────────────
            bool isEnabled;
            try
            {
                isEnabled = await featureService.IsEnabledAsync(tenantId, _featureName);
            }
            catch (Exception ex)
            {
                // Cache veya DB hatası — güvenli tarafa geç: erişimi reddet.
                // Üretim ortamında bu log kritiktir; sessiz yutma yapılmaz.
                var logger = httpContext.RequestServices
                    .GetRequiredService<ILogger<RequireFeatureAttribute>>();

                logger.LogError(ex,
                    "[RequireFeature] IsEnabledAsync fırlatıldı. " +
                    "Feature: {Feature} | TenantId: {TenantId} | " +
                    "Path: {Path}",
                    _featureName, tenantId, httpContext.Request.Path);

                SetDeniedResult(context, isAjax: IsAjaxRequest(httpContext));
                return;
            }

            if (isEnabled)
            {
                // Özellik aktif → pipeline'a devam et
                await next();
                return;
            }

            // ── Erişim Reddedildi ────────────────────────────────────────────
            SetDeniedResult(context, isAjax: IsAjaxRequest(httpContext));
        }

        // ── Yardımcı: AJAX tespiti ───────────────────────────────────────────
        /// <summary>
        /// İsteğin AJAX (fetch / XHR / JSON API) olup olmadığını belirler.
        /// İki koşuldan biri yeterliydi:
        ///   1. X-Requested-With: XMLHttpRequest  (jQuery / eski XHR)
        ///   2. Accept başlığı application/json içeriyor  (fetch API)
        /// </summary>
        private static bool IsAjaxRequest(HttpContext httpContext)
        {
            var request = httpContext.Request;

            // Koşul 1 — klasik XHR header
            if (request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return true;

            // Koşul 2 — modern fetch: Accept: application/json
            var accept = request.Headers["Accept"].ToString();
            if (!string.IsNullOrEmpty(accept) &&
                accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        // ── Yardımcı: Yanıt stratejisi ───────────────────────────────────────
        /// <summary>
        /// İstek tipine göre doğru reddetme sonucunu context'e atar.
        /// </summary>
        private void SetDeniedResult(
            ActionExecutingContext context,
            bool isAjax,
            string? reason = null)
        {
            if (isAjax)
            {
                // AJAX / JSON API → HTTP 403 + JSON yanıt
                // Frontend bu yanıtı yakalayıp kullanıcıya toast/modal gösterebilir.
                context.Result = new JsonResult(new
                {
                    success = false,
                    message = reason
                                   ?? "Bu özellik mevcut planınızda aktif değil.",
                    requiredPlan = "Pro",
                    feature = _featureName   // Debug için; production'da kaldırılabilir
                })
                { StatusCode = StatusCodes.Status403Forbidden };
            }
            else
            {
                // Normal sayfa isteği → Upgrade sayfasına yönlendir
                // SubscriptionController.Upgrade action'ı henüz yoksa Index'e düşer.
                context.Result = new RedirectToActionResult(
                    actionName: "Upgrade",
                    controllerName: "Subscription",
                    routeValues: new { area = "App" });
            }
        }
    }
}
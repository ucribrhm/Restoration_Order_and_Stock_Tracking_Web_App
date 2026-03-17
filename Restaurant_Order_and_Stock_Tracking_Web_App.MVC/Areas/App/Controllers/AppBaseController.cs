// ============================================================================
//  Areas/App/Controllers/AppBaseController.cs
//
//  Global Hard-Lock Sistemi:
//  Trial planı + TrialEndsAt süresi dolmuşsa tüm sayfa isteklerini
//  /App/Subscription/Upgrade sayfasına yönlendirir.
//  Bypass: Subscription ve Auth controller'ları + Impersonation oturumları.
// ============================================================================

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using System.Security.Claims;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.App.Controllers
{
    [Area("App")]
    [Authorize(AuthenticationSchemes = "AppAuth")]
    public abstract class AppBaseController : Controller
    {
        public override async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            // ── Sonsuz döngü koruması ────────────────────────────────────────
            // Subscription ve Auth controller'ları Hard-Lock'tan muaftır.
            var controllerName = context.RouteData.Values["controller"]?.ToString();
            if (controllerName is "Subscription" or "Auth")
            {
                await next();
                return;
            }

            // ── Impersonation Bypass ─────────────────────────────────────────
            // SysAdmin impersonation ile girmiş → trial/aktif durumuna bakılmaz.
            var isImpersonation = context.HttpContext.User
                .FindFirstValue("IsImpersonation") == "true";
            if (isImpersonation)
            {
                await next();
                return;
            }

            // ── TenantId ─────────────────────────────────────────────────────
            var tenantId = context.HttpContext.User.FindFirstValue("TenantId");
            if (string.IsNullOrEmpty(tenantId))
            {
                await next();
                return;
            }

            // ── DB'den tenant çek ────────────────────────────────────────────
            var db = context.HttpContext.RequestServices
                .GetRequiredService<RestaurantDbContext>();

            var tenant = await db.Tenants
                .AsNoTracking()
                .Select(t => new { t.TenantId, t.PlanType, t.TrialEndsAt, t.IsActive })
                .FirstOrDefaultAsync(t => t.TenantId == tenantId);

            // Pasif tenant → auth akışına bırak
            if (tenant is null || !tenant.IsActive)
            {
                await next();
                return;
            }

            // ── Hard-Lock: Trial + Süre Dolmuş ──────────────────────────────
            // Sadece trial planı için kontrol yapılır.
            // Starter ve Pro her zaman geçer.
            if (tenant.PlanType == "trial" &&
                tenant.TrialEndsAt.HasValue &&
                tenant.TrialEndsAt.Value < DateTime.UtcNow)
            {
                // /App/Subscription/Upgrade → plan seçim ve ödeme sayfası
                context.Result = new RedirectToActionResult(
                    actionName: "Upgrade",
                    controllerName: "Subscription",
                    routeValues: new { area = "App" });
                return;
            }

            await next();
        }
    }
}
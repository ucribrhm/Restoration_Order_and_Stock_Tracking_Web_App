// ============================================================================
//  Areas/App/Controllers/SubscriptionController.cs
// ============================================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;
using System.Security.Claims;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.App.Controllers
{
    public class SubscriptionController : AppBaseController
    {
        private readonly RestaurantDbContext _db;
        private readonly ITenantFeatureService _featureService;

        public SubscriptionController(
            RestaurantDbContext db,
            ITenantFeatureService featureService)
        {
            _db = db;
            _featureService = featureService;
        }

        // GET /App/Subscription/Index
        public async Task<IActionResult> Index()
        {
            var tenantId = User.FindFirstValue("TenantId");
            var tenant = await _db.Tenants.AsNoTracking()
                               .FirstOrDefaultAsync(t => t.TenantId == tenantId);
            ViewData["Title"] = "Abonelik Planları";
            ViewBag.RestaurantName = tenant?.Name ?? "";
            ViewBag.TrialEndsAt = tenant?.TrialEndsAt;
            ViewBag.PlanType = tenant?.PlanType ?? "trial";
            return View();
        }

        // GET /App/Subscription/Upgrade
        [HttpGet]
        public async Task<IActionResult> Upgrade()
        {
            var tenantId = User.FindFirstValue("TenantId");
            var tenant = await _db.Tenants.AsNoTracking()
                               .FirstOrDefaultAsync(t => t.TenantId == tenantId);
            ViewData["Title"] = "Pro'ya Geçin";
            ViewBag.RestaurantName = tenant?.Name ?? "";
            ViewBag.PlanType = tenant?.PlanType ?? "trial";
            return View();
        }

        // GET /App/Subscription/Checkout?plan=pro
        [HttpGet]
        public async Task<IActionResult> Checkout(string plan)
        {
            plan = (plan ?? "").ToLowerInvariant().Trim();
            if (plan is not ("starter" or "pro"))
                return RedirectToAction(nameof(Upgrade));

            var tenantId = User.FindFirstValue("TenantId");
            var tenant = await _db.Tenants.AsNoTracking()
                               .FirstOrDefaultAsync(t => t.TenantId == tenantId);

            ViewData["Title"] = "Ödeme";
            ViewBag.SelectedPlan = plan;
            ViewBag.RestaurantName = tenant?.Name ?? "";
            ViewBag.CurrentPlan = tenant?.PlanType ?? "trial";
            ViewBag.PlanPrice = plan == "pro" ? "₺999 / ay" : "₺499 / ay";
            ViewBag.PlanLabel = plan == "pro" ? "⚡ Pro" : "🌱 Starter";
            return View();
        }

        // POST /App/Subscription/ProcessPayment
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessPayment(
            string plan,
            string cardNumber,
            string cardHolder,
            string expiryDate,
            string cvv)
        {
            plan = (plan ?? "").ToLowerInvariant().Trim();
            if (plan is not ("starter" or "pro"))
                return RedirectToAction(nameof(Upgrade));

            // 2 saniye ödeme simülasyonu
            await Task.Delay(2000);

            var tenantId = User.FindFirstValue("TenantId");
            if (string.IsNullOrEmpty(tenantId))
                return RedirectToAction(nameof(Upgrade));

            var tenant = await _db.Tenants.FirstOrDefaultAsync(t => t.TenantId == tenantId);
            if (tenant is null)
                return RedirectToAction(nameof(Upgrade));

            tenant.PlanType = plan;
            tenant.TrialEndsAt = null;   // Ücretli planlarda trial süresi yok
            await _db.SaveChangesAsync();

            _featureService.InvalidateCache(tenantId);

            TempData["NewPlan"] = plan;
            return RedirectToAction(nameof(PaymentSuccess));
        }

        // GET /App/Subscription/PaymentSuccess
        [HttpGet]
        public IActionResult PaymentSuccess()
        {
            ViewData["Title"] = "Ödeme Başarılı";
            ViewBag.NewPlan = TempData["NewPlan"] as string ?? "pro";
            return View();
        }
    }
}
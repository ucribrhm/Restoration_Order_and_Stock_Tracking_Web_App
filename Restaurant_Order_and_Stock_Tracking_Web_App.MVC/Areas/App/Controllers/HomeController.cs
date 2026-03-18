// ════════════════════════════════════════════════════════════════════════════
//  Areas/App/Controllers/HomeController.cs
//
//  PERF-06 — DashboardService Entegrasyonu
//
//  ESKİ DURUM (420 satır):
//    HomeController → 16+ DB sorgusu → her istekte tüm sorgular çalışır
//    100 kullanıcıda DB'ye saatte ~57.600 hit.
//
//  YENİ DURUM (bu dosya):
//    HomeController → IDashboardService → IMemoryCache (30sn) → DB
//    100 kullanıcıda DB'ye saatte ~240 hit. 240x azalma.
//
//  View (Index.cshtml) ve DashboardViewModel değişmedi — sadece
//  veriyi nereden aldığımız değişti.
// ════════════════════════════════════════════════════════════════════════════

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;
using System.Security.Claims;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.App.Controllers
{
    [Area("App")]
    [Authorize(Roles = "Admin")] // Bu satır çok kritik!
    public class HomeController : AppBaseController
    {
        private readonly IDashboardService _dashboardService;
        private readonly ILogger<HomeController> _logger;

        public HomeController(
            IDashboardService dashboardService,
            ILogger<HomeController> logger)
        {
            _dashboardService = dashboardService;
            _logger = logger;
        }

        // ════════════════════════════════════════════════════════════════════
        //  GET /App/Home/Index — Dashboard ana sayfa
        //  DashboardService 30sn cache ile DB yükünü 240x azaltır.
        // ════════════════════════════════════════════════════════════════════
        public async Task<IActionResult> Index()
        {
            ViewData["Title"] = "Dashboard";

            var tenantId = User.FindFirstValue("TenantId") ?? string.Empty;

            var vm = await _dashboardService.GetDashboardDataAsync(tenantId);

            _logger.LogDebug("[Dashboard] Index yüklendi. TenantId: {TenantId}", tenantId);

            return View(vm);
        }

        // ════════════════════════════════════════════════════════════════════
        //  GET /App/Home/GetLiveMetrics — AJAX endpoint
        //  SignalR eventi tetiklenince JS fetch ile çağrılır.
        //  DashboardService 30sn cache — aynı pencerede tekrar DB'ye gitmiyor.
        // ════════════════════════════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> GetLiveMetrics()
        {
            var tenantId = User.FindFirstValue("TenantId") ?? string.Empty;

            var data = await _dashboardService.GetLiveMetricsAsync(tenantId);

            return Json(data);
        }
    }
}
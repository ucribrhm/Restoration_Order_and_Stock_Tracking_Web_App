// ============================================================================
//  Areas/Admin/Controllers/HomeController.cs
//  GÖREV 3 — SysAdmin Dashboard Controller
//
//  Güvenlik:
//  • AdminBaseController'dan türer → [Authorize(Roles="SysAdmin", AuthenticationSchemes="AdminAuth")]
//  • Herhangi bir Tenant filtresi YOKTUR — SysAdmin tüm DB'yi görür.
//    (TenantId claim eklenmediği için ITenantService.TenantId = null →
//     Global Query Filter "null == null" dalına girer → tüm kayıtlar döner)
//
//  Index Action:
//  • Tüm Tenant'ları çek (IgnoreQueryFilters gerekmez — null TenantId zaten bypass eder)
//  • Dashboard istatistiklerini hesapla
//  • Son 7 günün günlük kayıt dağılımını Chart.js için hazırla
// ============================================================================

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Admin;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.Admin.Controllers;

public class HomeController : AdminBaseController
{
    private readonly RestaurantDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITenantFeatureService _tenantFeatureService;

    // Türkçe ay kısaltmaları — Chart.js etiketleri için
    private static readonly string[] TurkishMonths =
        { "Oca", "Şub", "Mar", "Nis", "May", "Haz",
          "Tem", "Ağu", "Eyl", "Eki", "Kas", "Ara" };

    public HomeController(
        RestaurantDbContext db,
        UserManager<ApplicationUser> userManager,
        ITenantFeatureService tenantFeatureService)
    {
        _db = db;
        _userManager = userManager;
        _tenantFeatureService = tenantFeatureService;
    }

    // ── GET /Admin/Home/Index ────────────────────────────────────────────────
    public async Task<IActionResult> Index()
    {
        ViewData["Title"] = "Dashboard";

        // ════════════════════════════════════════════════════════════════════
        //  1. Tüm Tenant'ları çek
        //
        //  NOT: ITenantService.TenantId SysAdmin için null olduğundan
        //  Global Query Filter zaten tüm kayıtları döndürür.
        //  IgnoreQueryFilters() kullanmak GEREKMİYOR.
        // ════════════════════════════════════════════════════════════════════
        var allTenants = await _db.Tenants
            .AsNoTracking()
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        // ════════════════════════════════════════════════════════════════════
        //  2. Kullanıcı sayılarını çek (tenant başına)
        //     Tek sorguda GroupBy ile N+1 önlenir.
        // ════════════════════════════════════════════════════════════════════
        var userCountByTenant = await _userManager.Users
            .AsNoTracking()
            .Where(u => u.TenantId != null)      // SysAdmin'leri dışla
            .GroupBy(u => u.TenantId!)
            .Select(g => new { TenantId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TenantId, x => x.Count);

        var totalUsers = await _userManager.Users.CountAsync();

        // ════════════════════════════════════════════════════════════════════
        //  3. İstatistik Kartları
        // ════════════════════════════════════════════════════════════════════
        var now30DaysAgo = DateTime.UtcNow.AddDays(-30);

        var vm = new AdminDashboardViewModel
        {
            TotalTenants = allTenants.Count,
            ActiveTenants = allTenants.Count(t => t.IsActive),
            InactiveTenants = allTenants.Count(t => !t.IsActive),
            NewTenantsLast30Days = allTenants.Count(t => t.CreatedAt >= now30DaysAgo),
            TotalUsers = totalUsers,
        };

        // ════════════════════════════════════════════════════════════════════
        //  4. Son 7 Günlük Restoran Büyüme Grafiği (Chart.js)
        //
        //  Her gün için o gün kaç yeni restoran kaydolmuş → bar/line chart
        // ════════════════════════════════════════════════════════════════════
        var today = DateTime.UtcNow.Date;
        var weekAgo = today.AddDays(-6); // son 7 gün (bugün dahil)

        // DB tarafında tarih gruplama (PostgreSQL DATE() cast)
        var dailyCounts = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.CreatedAt >= weekAgo && t.CreatedAt < today.AddDays(1))
            .GroupBy(t => t.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Date, x => x.Count);

        for (var d = weekAgo; d <= today; d = d.AddDays(1))
        {
            var label = $"{d.Day} {TurkishMonths[d.Month - 1]}";
            vm.GrowthLabels.Add(label);
            vm.GrowthData.Add(dailyCounts.TryGetValue(d, out var cnt) ? cnt : 0);
        }

        // ════════════════════════════════════════════════════════════════════
        //  5. Restoran Listesi — tablo satırları
        // ════════════════════════════════════════════════════════════════════
        vm.Tenants = allTenants.Select(t => new TenantRowViewModel
        {
            TenantId = t.TenantId,
            Name = t.Name,
            Subdomain = t.Subdomain,
            PlanType = t.PlanType,
            CreatedAt = t.CreatedAt,
            IsActive = t.IsActive,
            UserCount = userCountByTenant.TryGetValue(t.TenantId, out var uc) ? uc : 0,
        }).ToList();

        return View(vm);
    }

    // ── POST /Admin/Home/ChangePlan ──────────────────────────────────────────
    // Plan değişince DB güncellenir + feature cache temizlenir.
    // TenantFeatureService artık TenantConfig bool'larını okumaz —
    // yetki tamamen PlanType + TrialEndsAt'a göre hesaplanır.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePlan([FromBody] ChangePlanDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.TenantId) ||
            string.IsNullOrWhiteSpace(dto.NewPlanType))
            return Json(new { success = false, message = "TenantId ve NewPlanType zorunludur." });

        // Sadece 3 geçerli plan — enterprise yok
        var validPlans = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "trial", "starter", "pro" };

        if (!validPlans.Contains(dto.NewPlanType))
            return Json(new { success = false, message = $"Geçersiz plan tipi: '{dto.NewPlanType}'. Geçerliler: trial, starter, pro" });

        var tenant = await _db.Tenants
            .FirstOrDefaultAsync(t => t.TenantId == dto.TenantId);

        if (tenant is null)
            return Json(new { success = false, message = "Restoran bulunamadı." });

        var plan = dto.NewPlanType.ToLowerInvariant();
        tenant.PlanType = plan;

        // ── TrialEndsAt mantığı ─────────────────────────────────────────────
        // trial  → 15 gün ver (veya mevcut süreyi uzat)
        // starter / pro → trial süresi yok
        if (plan == "trial")
        {
            // Mevcut trial bitmişse yeni 15 gün ver, bitmemişse uzatma
            if (!tenant.TrialEndsAt.HasValue || tenant.TrialEndsAt.Value < DateTime.UtcNow)
                tenant.TrialEndsAt = DateTime.UtcNow.AddDays(15);
            // Bitmemişse dokunma — kalan süre korunsun
        }
        else
        {
            tenant.TrialEndsAt = null; // Ücretli planlarda trial süresi yok
        }

        await _db.SaveChangesAsync();

        // ── Feature cache'i temizle ──────────────────────────────────────────
        // SaveChangesAsync'ten HEMEN SONRA cache temizlenir.
        // Kullanıcı sayfayı yenileyince MapToDto yeni PlanType ile çalışır.
        // Inject edilmiş field kullanılır — RequestServices scope riski yok.
        _tenantFeatureService.InvalidateCache(dto.TenantId);

        return Json(new
        {
            success = true,
            message = $"'{tenant.Name}' planı '{plan.ToUpperInvariant()}' olarak güncellendi.",
            newPlanType = plan
        });
    }
}

// ── DTO (iç sınıf — ayrı dosyaya taşınabilir) ───────────────────────────────
// Dosya yolu: Areas/Admin/Controllers/HomeController.cs (iç sınıf olarak)
// ya da ViewModels/Admin/ChangePlanDto.cs olarak ayrı bir dosyaya alınabilir.
public sealed record ChangePlanDto(string TenantId, string NewPlanType);
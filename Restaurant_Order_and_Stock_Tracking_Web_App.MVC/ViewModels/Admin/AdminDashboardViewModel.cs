// ============================================================================
//  ViewModels/Admin/AdminDashboardViewModel.cs
//  GÖREV 3 — SysAdmin Dashboard View Model
//
//  HomeController.Index() bu modeli doldurarak Dashboard view'ına geçirir.
//  Tüm veriler SysAdmin perspektifinden (tüm tenant'lar) hesaplanır.
// ============================================================================

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Admin;

public class AdminDashboardViewModel
{
    // ── İstatistik Kartları ──────────────────────────────────────────────────
    /// <summary>Sistemdeki toplam restoran sayısı.</summary>
    public int TotalTenants { get; set; }

    /// <summary>IsActive = true olan restoran sayısı.</summary>
    public int ActiveTenants { get; set; }

    /// <summary>IsActive = false olan restoran sayısı.</summary>
    public int InactiveTenants { get; set; }

    /// <summary>Son 30 günde kayıt olan restoran sayısı.</summary>
    public int NewTenantsLast30Days { get; set; }

    /// <summary>Sistemdeki toplam kullanıcı sayısı (tüm tenant'lar + SysAdmin).</summary>
    public int TotalUsers { get; set; }

    // ── Chart.js — Son 7 Günlük Restoran Kayıt Grafiği ──────────────────────
    /// <summary>X ekseni: son 7 günün kısa tarih etiketleri. ["4 Mar", "5 Mar", ...]</summary>
    public List<string> GrowthLabels { get; set; } = [];

    /// <summary>Y ekseni: her gün için kümülatif değil anlık kayıt sayısı.</summary>
    public List<int> GrowthData { get; set; } = [];

    // ── Restoran Listesi Tablosu ─────────────────────────────────────────────
    /// <summary>Tüm tenant'ların listesi — en yeni kayıt önce sıralı.</summary>
    public List<TenantRowViewModel> Tenants { get; set; } = [];
}

/// <summary>
/// Dashboard tablosunda her restoran için bir satır.
/// </summary>
public class TenantRowViewModel
{
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string PlanType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }

    /// <summary>Bu tenant'a ait kullanıcı sayısı.</summary>
    public int UserCount { get; set; }
}
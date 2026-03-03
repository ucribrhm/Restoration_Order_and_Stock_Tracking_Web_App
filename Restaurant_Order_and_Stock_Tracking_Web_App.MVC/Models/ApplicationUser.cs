// ============================================================================
//  Models/ApplicationUser.cs
//  DEĞİŞİKLİK — FAZ 1 ADIM 2: Multi-Tenancy
//
//  EKLENEN: TenantId (nullable string)
//  → Kullanıcının hangi restorana bağlı olduğunu gösterir.
//  → null yalnızca sistem geneli süper admin için kabul edilebilir.
//  → Giriş yapıldığında TenantClaimsTransformation bu değeri Claims'e yazar.
//
//  DİĞER TÜM ALANLAR AYNEN KORUNDU.
// ============================================================================
using Microsoft.AspNetCore.Identity;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    // PhoneNumber zaten IdentityUser'dan geliyor

    // ── FAZ 1 ADIM 2: Multi-Tenancy ─────────────────────────────────────────
    /// <summary>
    /// Bu kullanıcının ait olduğu restoranın TenantId'si.
    /// FK → tenants.TenantId (DbContext'te konfigüre edildi).
    ///
    /// null: Sistem geneli süper admin (tüm tenant verilerine erişebilir).
    /// Non-null: Sadece bu tenant'a ait verilere erişebilir.
    ///
    /// Kullanıcı oluşturulurken (Onboard veya Admin paneli) atanmalıdır.
    /// TenantClaimsTransformation login sırasında bunu Claims'e ekler.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>Navigasyon. EF Core ilişkisi DbContext'te tanımlı.</summary>
    public virtual Tenant? Tenant { get; set; }
}
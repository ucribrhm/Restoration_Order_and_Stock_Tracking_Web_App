// ============================================================================
//  Services/TenantOnboardingService.cs
//  SaaS Onboarding — Yeni Restoran Atomik Kayıt Servisi
//
//  İşlem sırası (tamamı tek BeginTransactionAsync içinde):
//    1. Restoran adından URL-safe TenantId slug üret  (ibo-kafe-a1b2)
//    2. Subdomain benzersizliğini kontrol et
//    3. Admin rolünü güvence altına al (yoksa oluştur)
//    4. Tenants tablosuna kayıt at
//    5. TenantConfig oluştur
//    6. ApplicationUser oluştur + Admin rolü ata
//    7. Commit → başarı döndür
//    Herhangi bir adımda hata → Rollback → hata mesajı döndür
// ============================================================================

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Data;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;

public class TenantOnboardingService : ITenantOnboardingService
{
    private readonly RestaurantDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public TenantOnboardingService(
        RestaurantDbContext db,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager)
    {
        _db = db;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<(bool Success, string? TenantId, string? Error)> CreateTenantAsync(
        TenantOnboardingDto dto)
    {
        // ── Transaction başlat ────────────────────────────────────────────
        await using var tx = await _db.Database.BeginTransactionAsync();

        try
        {
            // ── Adım 1: TenantId slug üret ────────────────────────────────
            // 'İbo Kafe' → 'ibo-kafe' + '-' + Guid'in ilk 8 karakteri
            // Kısa GUID son eki çakışma olasılığını ortadan kaldırır.
            var baseSlug = GenerateSlug(dto.RestaurantName);
            var suffix = Guid.NewGuid().ToString("N")[..8];   // 8 hex karakter
            var tenantId = $"{baseSlug}-{suffix}";              // ibo-kafe-a1b2c3d4

            // ── Adım 2: Subdomain benzersizliği ───────────────────────────
            var subdomainExists = await _db.Tenants
                .AnyAsync(t => t.Subdomain == dto.Subdomain);
            if (subdomainExists)
                return (false, null, $"'{dto.Subdomain}' subdomain'i zaten kullanımda. Farklı bir subdomain seçin.");

            // Kullanıcı adı çakışması
            if (await _userManager.FindByNameAsync(dto.AdminUsername) != null)
                return (false, null, $"'{dto.AdminUsername}' kullanıcı adı zaten alınmış. Farklı bir kullanıcı adı seçin.");

            // ── Adım 3: Admin rolünü güvence altına al ───────────────────
            if (!await _roleManager.RoleExistsAsync("Admin"))
                await _roleManager.CreateAsync(new IdentityRole("Admin"));

            // ── Adım 4: Tenant kaydı ──────────────────────────────────────
            var tenant = new Tenant
            {
                TenantId = tenantId,
                Name = dto.RestaurantName,
                Subdomain = dto.Subdomain,
                PlanType = "trial",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                TrialEndsAt = DateTime.UtcNow.AddDays(30),
                RestaurantType = RestaurantType.CasualDining,
            };
            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync();

            // ── Adım 5: TenantConfig ──────────────────────────────────────
            var config = new TenantConfig
            {
                TenantId = tenantId,
                EnableKitchenDisplay = true,
                EnableReservations = true,
                EnableDiscounts = true,
                EnableTableMerge = false,
                EnableSelfOrderQr = false,
                CurrencyCode = "TRY",
            };
            _db.TenantConfigs.Add(config);
            await _db.SaveChangesAsync();

            // ── Adım 6: Admin kullanıcısı ─────────────────────────────────
            var adminUser = new ApplicationUser
            {
                UserName = dto.AdminUsername,
                FullName = dto.FullName,
                Email = dto.Email,
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow,
                TenantId = tenantId,   // FK — Global Query Filter için zorunlu
            };

            var createResult = await _userManager.CreateAsync(adminUser, dto.Password);
            if (!createResult.Succeeded)
            {
                var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                await tx.RollbackAsync();
                return (false, null, $"Kullanıcı oluşturulamadı: {errors}");
            }

            var roleResult = await _userManager.AddToRoleAsync(adminUser, "Admin");
            if (!roleResult.Succeeded)
            {
                var errors = string.Join(", ", roleResult.Errors.Select(e => e.Description));
                await tx.RollbackAsync();
                return (false, null, $"Rol ataması başarısız: {errors}");
            }

            // ── Adım 7: Commit ────────────────────────────────────────────
            await tx.CommitAsync();
            return (true, tenantId, null);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            return (false, null, $"Kayıt sırasında beklenmeyen hata oluştu: {ex.Message}");
        }
    }

    // ── Yardımcı: Restoran adından URL-safe slug üret ─────────────────────
    // 'İbo Kafe & Restoran!'  →  'ibo-kafe-restoran'
    private static string GenerateSlug(string input)
    {
        // 1. Unicode normalleştir ve aksan karakterlerini ASCII'ye çevir
        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        // 2. Türkçe özgün karakterleri elle çevir (normalizasyonun kapsamadıkları)
        var result = sb.ToString()
            .Replace('ı', 'i')
            .Replace('İ', 'i')
            .Replace('ğ', 'g')
            .Replace('Ğ', 'g')
            .Replace('ş', 's')
            .Replace('Ş', 's')
            .Replace('ö', 'o')
            .Replace('Ö', 'o')
            .Replace('ü', 'u')
            .Replace('Ü', 'u')
            .Replace('ç', 'c')
            .Replace('Ç', 'c');

        // 3. Küçük harf, alfanümerik dışı karakterleri tire'ye çevir
        result = result.ToLowerInvariant();
        result = Regex.Replace(result, @"[^a-z0-9\s-]", "");   // özel karakter sil
        result = Regex.Replace(result, @"\s+", "-");             // boşluk → tire
        result = Regex.Replace(result, @"-+", "-");              // çoklu tire → tek tire
        result = result.Trim('-');                               // baş/son tire sil

        // 4. Boş kaldıysa güvenli fallback
        if (string.IsNullOrEmpty(result))
            result = "restoran";

        // 5. Uzunluk sınırı (Guid suffix + tire için yer bırak)
        if (result.Length > 40)
            result = result[..40].TrimEnd('-');

        return result;
    }
}
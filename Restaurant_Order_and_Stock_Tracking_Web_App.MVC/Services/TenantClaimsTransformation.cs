// ============================================================================
//  Services/TenantClaimsTransformation.cs
//  SaaS Çok Kiracılı Yapı — Tenant Bilgisini Claims'e Yazan Dönüştürücü
//
//  Bu sınıf ASP.NET Core'un IClaimsTransformation mekanizmasını kullanır.
//  Her kimlik doğrulama olayından sonra (login, cookie yenileme) otomatik
//  olarak çağrılır.
//
//  GÖREV:
//    ApplicationUser.TenantId değerini DB'den okuyup "TenantId" adlı bir
//    Claim olarak kullanıcının ClaimsPrincipal'ına ekler.
//
//  Bu sayede HttpContextTenantService DB'ye hiç dokunmadan Claim'i okur.
//  Döngüsel bağımlılık problemi bu sınıf sayesinde çözülür.
//
//  Program.cs'e eklenecek kayıt:
//    builder.Services.AddScoped<IClaimsTransformation, TenantClaimsTransformation>();
// ============================================================================
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services
{
    public class TenantClaimsTransformation : IClaimsTransformation
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public TenantClaimsTransformation(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        /// <summary>
        /// Kullanıcı kimliği doğrulandığında çağrılır.
        /// "TenantId" Claim'i yoksa DB'den okuyup ekler.
        /// Claim zaten varsa tekrar DB'ye gitme — idempotent davranış.
        /// </summary>
        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            // Kullanıcı kimliği doğrulanmamışsa dokunma
            if (principal.Identity?.IsAuthenticated != true)
                return principal;

            // Claim zaten varsa tekrar DB'ye gitme (performans)
            if (principal.HasClaim(c => c.Type == "TenantId"))
                return principal;

            // DB'den kullanıcıyı al ve TenantId'yi oku
            var user = await _userManager.GetUserAsync(principal);
            if (user?.TenantId is null)
                return principal; // Süper admin veya TenantId atanmamış kullanıcı

            // Yeni bir kimlik oluştur ve TenantId Claim'ini ekle
            var claimsIdentity = new ClaimsIdentity();
            claimsIdentity.AddClaim(new Claim("TenantId", user.TenantId));

            principal.AddIdentity(claimsIdentity);

            return principal;
        }
    }
}
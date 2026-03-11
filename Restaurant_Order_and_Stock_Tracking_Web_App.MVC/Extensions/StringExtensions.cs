// ════════════════════════════════════════════════════════════════════════════
//  Extensions/StringExtensions.cs
//  Yol: Restaurant_Order_and_Stock_Tracking_Web_App.MVC/Extensions/
//
//  SPRINT B — [SB-2] Workspace Login Prefix Maskeleme
//
//  ToDisplayName(rawUsername, tenantId)
//    DB'de "{tenantId}_{kısaAd}" formatında saklanan kullanıcı adından
//    tenant prefix'ini soyarak yalnızca kısa adı döndürür.
//
//  Kullanım:
//    "burger-palace-a1b2_ahmet".ToDisplayName("burger-palace-a1b2") → "ahmet"
//    "ahmet".ToDisplayName("burger-palace-a1b2")                    → "ahmet"
//    "".ToDisplayName("burger-palace-a1b2")                         → ""
// ════════════════════════════════════════════════════════════════════════════

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Extensions;

public static class StringExtensions
{
    /// <summary>
    /// Workspace Login prefix'ini (<c>tenantId_</c>) kullanıcı adından soyar.
    /// Prefix yoksa orijinal değeri döndürür — idempotent ve null-safe.
    /// </summary>
    /// <param name="rawUsername">DB'deki ham kullanıcı adı (ör: "burger-palace-a1b2_ahmet")</param>
    /// <param name="tenantId">Aktif tenant kimliği (ör: "burger-palace-a1b2")</param>
    /// <returns>Görüntülenecek kısa ad (ör: "ahmet")</returns>
    public static string ToDisplayName(this string rawUsername, string? tenantId)
    {
        if (string.IsNullOrEmpty(rawUsername))
            return rawUsername;

        if (string.IsNullOrEmpty(tenantId))
            return rawUsername;

        // Prefix: "burger-palace-a1b2_"
        var prefix = tenantId + "_";

        return rawUsername.StartsWith(prefix, StringComparison.Ordinal)
            ? rawUsername[prefix.Length..]   // prefix'den sonrasını al
            : rawUsername;                   // prefix yoksa olduğu gibi dön
    }
}
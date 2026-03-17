// ============================================================================
//  TagHelpers/FeatureTagHelper.cs
//
//  Kullanım:
//    ① Özellik aktifse → içeriği normal render et
//    ② Özellik kapalı + locked="false" (default) → içeriği DOM'dan tamamen sil
//    ③ Özellik kapalı + locked="true" → içeriği soluk göster + PRO rozeti ekle
//
//  ÖRNEKLER:
//    <!-- Özellik yoksa menüden tamamen gizle -->
//    <feature name="@Features.SplitBill">
//        <a asp-action="SplitBill">Hesap Böl</a>
//    </feature>
//
//    <!-- Özellik yoksa soluklaştır, PRO rozeti ekle (upsell) -->
//    <feature name="@Features.TableMerge" locked="true">
//        <button>Masa Birleştir</button>
//    </feature>
//
//  BYPASS:
//    SysAdmin ve Impersonation oturumları tüm kısıtlamaları atlar.
// ============================================================================

using Microsoft.AspNetCore.Razor.TagHelpers;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Shared;
using Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Services;
using System.Security.Claims;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.TagHelpers
{
    /// <summary>
    /// Tenant özellik bayrağına göre UI bileşenlerini gösteren, gizleyen
    /// veya kilitli olarak işaretleyen Razor Tag Helper.
    /// </summary>
    [HtmlTargetElement("feature")]
    public sealed class FeatureTagHelper : TagHelper
    {
        // ── DI Bağımlılıkları ────────────────────────────────────────────────
        private readonly ITenantFeatureService _featureService;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public FeatureTagHelper(
            ITenantFeatureService featureService,
            IHttpContextAccessor httpContextAccessor)
        {
            _featureService = featureService;
            _httpContextAccessor = httpContextAccessor;
        }

        // ── Tag Özellikleri ─────────────────────────────────────────────────

        /// <summary>
        /// Kontrol edilecek özellik adı (zorunlu).
        /// Yazım güvenliği için <see cref="Features"/> sabitlerini kullanın.
        /// Örn: name="@Features.KDS"
        /// </summary>
        [HtmlAttributeName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// false (varsayılan): Özellik kapalıysa içerik tamamen gizlenir.
        /// true: Özellik kapalıysa içerik soluk gösterilir + PRO rozeti eklenir.
        /// </summary>
        [HtmlAttributeName("locked")]
        public bool Locked { get; set; } = false;

        // ── Render Mantığı ──────────────────────────────────────────────────
        public override async Task ProcessAsync(
            TagHelperContext context,
            TagHelperOutput output)
        {
            // <feature> elementi kendisi DOM'a yazılmaz — içeriği direkt render et
            output.TagName = null;

            // ── Temel doğrulama ──────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(Name))
            {
                // name attribute boş bırakılmış → sessizce içeriği render et
                // (Geliştirici hatası; production'da log basılabilir)
                return;
            }

            var httpContext = _httpContextAccessor.HttpContext;

            // HttpContext yoksa (unit test / middleware dışı) → güvenli taraf: gizle
            if (httpContext is null)
            {
                output.SuppressOutput();
                return;
            }

            var user = httpContext.User;

            // ── SysAdmin Bypass ──────────────────────────────────────────────
            // İmpersonation ile girilmiş veya SysAdmin rolü → tüm içerik görünür
            var isImpersonation = user.FindFirstValue("IsImpersonation") == "true";
            var isSysAdmin = user.IsInRole("SysAdmin");

            if (isImpersonation || isSysAdmin)
            {
                // İçeriği normal render et, bypass
                return;
            }

            // ── TenantId çözümleme ───────────────────────────────────────────
            var tenantId = user.FindFirstValue("TenantId");

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                // TenantId yoksa → özelliğe sahip değil gibi davran
                HandleDisabled(output, await output.GetChildContentAsync());
                return;
            }

            // ── Özellik kontrolü ────────────────────────────────────────────
            bool isEnabled;
            try
            {
                isEnabled = await _featureService.IsEnabledAsync(tenantId, Name);
            }
            catch
            {
                // Servis hatası → güvenli taraf: gizle
                output.SuppressOutput();
                return;
            }

            if (isEnabled)
            {
                // ✅ Özellik aktif → içeriği normal render et
                return;
            }

            // ── Özellik kapalı ───────────────────────────────────────────────
            var childContent = await output.GetChildContentAsync();
            HandleDisabled(output, childContent);
        }

        // ── Yardımcı: Kapalı özellik render stratejisi ──────────────────────
        private void HandleDisabled(TagHelperOutput output, TagHelperContent childContent)
        {
            if (!Locked)
            {
                // locked="false" → içeriği tamamen DOM'dan sil
                output.SuppressOutput();
                return;
            }

            // locked="true" → içeriği soluklaştır + PRO rozeti ekle
            //
            // Wrapper <span> ile sarılır:
            //   • position:relative   → rozeti mutlak konumlandırmak için
            //   • opacity:.45         → soluk görünüm
            //   • pointer-events:none → tıklanamaz
            //   • cursor:not-allowed  → yasaklı cursor
            //   • user-select:none    → metin seçilemesin
            //
            // PRO rozeti sayfanın üst köşesine eklenir (inline-flex).
            // Renk paleti theme.css CSS değişkenlerini kullanır.

            output.TagName = "span";
            output.Attributes.SetAttribute("style",
                "position:relative;" +
                "display:inline-flex;" +
                "align-items:center;" +
                "gap:6px;" +
                "opacity:.45;" +
                "pointer-events:none;" +
                "cursor:not-allowed;" +
                "user-select:none;");

            // PRO rozeti HTML
            const string proBadgeHtml =
                "<span style=\"" +
                    "display:inline-flex;" +
                    "align-items:center;" +
                    "font-size:.65rem;" +
                    "font-weight:800;" +
                    "letter-spacing:.6px;" +
                    "text-transform:uppercase;" +
                    "padding:2px 8px;" +
                    "border-radius:99px;" +
                    "background:rgba(168,85,247,.18);" +
                    "color:#c084fc;" +
                    "border:1px solid rgba(168,85,247,.35);" +
                    "white-space:nowrap;" +
                    "vertical-align:middle;" +
                "\">" +
                "⚡ PRO" +
                "</span>";

            // İçerik + rozeti birleştir
            output.Content.SetHtmlContent(
                childContent.GetContent() + proBadgeHtml);
        }
    }
}
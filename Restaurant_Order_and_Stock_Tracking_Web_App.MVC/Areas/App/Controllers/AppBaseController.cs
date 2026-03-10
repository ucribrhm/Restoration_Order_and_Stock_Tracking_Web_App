// ============================================================================
//  Areas/App/Controllers/AppBaseController.cs
//  SaaS Areas Mimarisi — App Area Temel Denetleyici
//
//  Tüm App Area controller'ları (operasyon paneli) bu sınıftan türer.
//
//  [Area("App")]
//    → MVC route sistemine bu controller'ın "App" area'sına ait olduğunu bildirir.
//    → Views arama yolu: Areas/App/Views/{Controller}/{Action}.cshtml
//
//  [Authorize(AuthenticationSchemes = "AppAuth")]
//    → SADECE "AppAuth" scheme'i ile oluşturulmuş cookie kabul edilir.
//    → AdminAuth cookie'si (SysAdmin) bu endpoint'e erişemez.
//    → Rol kısıtı burada kasıtlı YOKTUR: Farklı controller'lar
//      farklı rollere ihtiyaç duyar (Admin, Garson, Kasiyer, Kitchen).
//      Rol kontrolü alt sınıflarda [Authorize(Roles = "...")] ile yapılır.
//
//  Örnek alt sınıf kullanımı:
//    [Authorize(Roles = "Admin")]          ← sadece Admin
//    public class MenuController : AppBaseController { }
//
//    [Authorize(Roles = "Admin,Kasiyer")]  ← Admin veya Kasiyer
//    public class ReportsController : AppBaseController { }
//
//    public class TablesController : AppBaseController { }  ← AppAuth yeterli
// ============================================================================

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.App.Controllers
{
    [Area("App")]
    [Authorize(AuthenticationSchemes = "AppAuth")]
    public abstract class AppBaseController : Controller
    {
        // Tüm App area controller'ları bu base'den türer.
        // Rol bazlı kısıtlar alt sınıflarda [Authorize(Roles = "...")] ile eklenir.
    }
}
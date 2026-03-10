// ============================================================================
//  Areas/Admin/Controllers/AdminBaseController.cs
//  SaaS Areas Mimarisi — Admin Area Temel Denetleyici
//
//  Tüm Admin Area controller'ları bu sınıftan türer.
//
//  [Area("Admin")]
//    → MVC route sistemine bu controller'ın "Admin" area'sına ait olduğunu bildirir.
//    → Views arama yolu: Areas/Admin/Views/{Controller}/{Action}.cshtml
//
//  [Authorize(AuthenticationSchemes = "AdminAuth", Roles = "SysAdmin")]
//    → SADECE "AdminAuth" scheme'i ile oluşturulmuş cookie kabul edilir.
//    → AppAuth cookie'si (Garson, Kasiyer, vb.) bu endpoint'e teknik olarak
//      izin VERMEZ — farklı bir scheme olduğu için ASP.NET Core tarafından
//      okunmaz bile.
//    → Rol kontrolü: SysAdmin olmayan kullanıcılar AccessDenied'a yönlenir.
//      (AdminAuth.AccessDeniedPath = "/Admin/Auth/AccessDenied")
// ============================================================================

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(AuthenticationSchemes = "AdminAuth", Roles = "SysAdmin")]
    public abstract class AdminBaseController : Controller
    {
        // Tüm Admin area controller'ları bu base'den türer.
        // Controller özelinde ek rol veya politika kısıtları
        // alt sınıflarda [Authorize] ile eklenebilir.
    }
}
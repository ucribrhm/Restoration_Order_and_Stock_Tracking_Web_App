namespace Restoration_Order_and_Stock_Tracking_Web_App.MVC.Models;

public class Categories
{
    public Categories()
    {
        CategoryId = Guid.CreateVersion7();
    }
    public  Guid  CategoryId { get; set; }
    public string    CategoryName { get; set; }
    public int  CategorySortOrder { get; set; }
    public bool IsActive { get; set; }
}

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;

public class Category
{
    public  int  CategoryId { get; set; }
    public string CategoryName { get; set; }
    public int CategorySortOrder { get; set; }
    public bool IsActive { get; set; }

    public virtual ICollection<MenuItem> MenuItems { get; set; }

}



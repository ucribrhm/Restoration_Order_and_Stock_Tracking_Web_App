namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;

public class OrderItem
{
    public int OrderItemId { get; set; }
    public int OrderId { get; set; }
    public virtual Order Order { get; set; }
    public int MenuItemId { get; set; }
    public virtual MenuItem MenuItem { get; set; }
    public int OrderItemQuantity { get; set; }
    public decimal OrderItemUnitPrice { get; set; }
    public decimal OrderItemLineTotal { get; set; }
    public string OrderItemNote { get; set; }
    public string OrderItemStatus { get; set; }
    public DateTime OrderItemAddedAt { get; set; }
}


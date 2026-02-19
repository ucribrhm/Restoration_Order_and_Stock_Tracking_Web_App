namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;

public class Order
{
    public int OrderId { get; set; }
    public int TableId { get; set; }
    public virtual Table Table { get; set; }
    public string OrderStatus { get; set; }
    public string OrderOpenedBy { get; set; }
    public string OrderNote { get; set; }
    public decimal OrderTotalAmount { get; set; }
    public DateTime OrderOpenedAt { get; set; }
    public DateTime OrderClosedAt { get; set; }
    public virtual ICollection<OrderItem> OrderItems { get; set; }
    public virtual ICollection<Payment> Payments { get; set; }

}


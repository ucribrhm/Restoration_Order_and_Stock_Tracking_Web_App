namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models;

public class Payment
{
    public int PaymentId { get; set; }
    public int OrderId { get; set; }
    public virtual Order Order { get; set; }
    public int PaymentsMethod { get; set; }// 0 nakit 1 kart 
    public decimal PaymentsAmount { get; set; }
    public decimal PaymentsChangeGiven { get; set; }
    public DateTime PaymentsPaidAt { get; set; }
    public string PaymentsNote { get; set; }
}

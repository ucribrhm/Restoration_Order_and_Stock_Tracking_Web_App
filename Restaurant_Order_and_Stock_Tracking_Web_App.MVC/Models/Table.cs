namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models
{
    public class Table
    {
        public int TableId { get; set; }
        public string TableName { get; set; }
        public int TableCapacity { get; set; }
        public int TableStatus { get; set; }
        public DateTime TableCreatedAt { get; set; }
    }
}

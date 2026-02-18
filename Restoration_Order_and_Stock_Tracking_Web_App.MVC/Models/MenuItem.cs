namespace Restoration_Order_and_Stock_Tracking_Web_App.MVC.Models
{
    public class MenuItem
    {
        public int MenuItemId { get; set; }
        public int CategoryId { get; set; }
        public virtual Category Category { get; set; } // virtual olma nedeni nedir = arkada halihazırda performans amaçlı query tutar(lazy loading)
        public string MenuItemName { get; set; }
        public decimal MenuItemPrice { get; set; }
        public int StockQuantity { get; set; }
        public bool TrackStock { get; set; }
        public bool IsAvailable { get; set; }
        public string Description { get; set; }
        public DateTime MenuItemCreatedTime { get; set; }
    }
}

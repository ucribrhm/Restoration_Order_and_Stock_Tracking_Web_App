namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Models
{
    public class Table
    {
        public int TableId { get; set; }
        public string TableName { get; set; }
        public int TableCapacity { get; set; }
        public int TableStatus { get; set; }
        public DateTime TableCreatedAt { get; set; }

        public string? ReservationName { get; set; }
        public string? ReservationPhone { get; set; }
        public int? ReservationGuestCount { get; set; }
        public DateTime? ReservationTime { get; set; }

        /// <summary>
        /// Müşteri "Garson Çağır"a bastığında true olur.
        /// Garson "İlgilenildi" dediğinde false'a döner.
        /// </summary>
        public bool IsWaiterCalled { get; set; } = false;

        /// <summary>
        /// Garson çağrısının başladığı UTC zaman damgası.
        /// DismissWaiter çağrıldığında null'a döner.
        /// SLA takibi (10 dk ihlal) için kullanılır.
        /// </summary>
        public DateTime? WaiterCalledAt { get; set; }   // ← YENİ

        public virtual ICollection<Order> Orders { get; set; }
    }
}
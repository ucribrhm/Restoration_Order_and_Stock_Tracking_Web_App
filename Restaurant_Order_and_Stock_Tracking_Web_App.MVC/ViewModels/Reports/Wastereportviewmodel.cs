// ViewModels/Reports/Wastereportviewmodel.cs
namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.ViewModels.Reports
{
    public class WasteItemDto
    {
        public string ProductName { get; set; } = "";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal? CostPrice { get; set; }
        public decimal TotalLoss { get; set; }
        public DateTime Date { get; set; }
        public string Note { get; set; } = "";
        public string CancelReason { get; set; } = "";
        public string SourceType { get; set; } = "";
        public int? OrderId { get; set; }
    }

    public class TopWasteProductDto
    {
        public string ProductName { get; set; } = "";
        public int TotalQuantity { get; set; }
        public decimal TotalLoss { get; set; }
    }

    public class WasteReportViewModel
    {
        public DateRangeFilter Filter { get; set; } = new();

        public decimal TotalWasteLoss { get; set; }

        /// <summary>Fire olarak zayi edilen toplam ürün adedi (SiparişKaynaklı + StokKaynaklı)</summary>
        public int TotalWasteCount { get; set; }

        /// <summary>Stoka iade edilen toplam ürün adedi (IsWasted=false olan iptal kalemleri)</summary>
        public int TotalReturnCount { get; set; }   // ← YENİ: TotalRefundedToStock (tutar) ile karıştırılmamalı

        public List<WasteItemDto> OrderWastes { get; set; } = new();
        public List<WasteItemDto> StockLogWastes { get; set; } = new();
        public List<TopWasteProductDto> TopWasteProducts { get; set; } = new();

        /// <summary>Stoka iade edilen toplam TUTAR (adet × birim fiyat)</summary>
        public decimal TotalRefundedToStock { get; set; }

        public decimal OrderWasteTotal { get; set; }
        public decimal StockLogWasteTotal { get; set; }
    }
}
// ============================================================================
//  Migrations/AddPerf03Indexes.cs
//
//  PERF-03 — Kritik Performans Index'leri
//
//  Eklenen index'ler:
//    1. ix_orders_closedat          — Rapor tarih aralığı sorgularını hızlandırır
//       ReportsController'da WHERE order_closed_at BETWEEN ? AND ? her raporda
//       bu kolonu sequential scan yapıyordu. 10.000 sipariş = tam tablo taraması.
//
//    2. ix_stock_logs_createdat     — Stok/fire raporu tarih filtresi
//       StockLogs WHERE created_at BETWEEN ? AND ? sorgularını hızlandırır.
//       MovementCategory filtresi ile birlikte partial index yerine tam index
//       daha esnek (farklı MovementCategory kombinasyonlarında da çalışır).
//
//    3. ix_order_items_menuitemid   — "En çok satan ürünler" raporu
//       ZAten HasIndex("MenuItemId") vardı ama OrderId ile composite index olarak
//       tanımlıydı (ix_order_items_orderid). MenuItemId için bağımsız index
//       GROUP BY menu_item_id sorgularını (top ürün raporu) optimize eder.
//
//  NOT: order_items.MenuItemId için snapshot'ta HasIndex("MenuItemId") zaten var
//  ama ix_order_items_orderid adıyla kaydedilmiş — bu ayrı bir index.
//
//  Geri alma: dotnet ef migrations remove (Down() metodu mevcut)
// ============================================================================

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Migrations
{
    /// <inheritdoc />
    public partial class AddPerf03Indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. orders(order_closed_at) ────────────────────────────────────
            // EF Core snake_case convention: OrderClosedAt → order_closed_at
            // Rapor sorgularında: WHERE order_closed_at >= @from AND order_closed_at < @to
            migrationBuilder.CreateIndex(
                name: "ix_orders_closedat",
                table: "orders",
                column: "order_closed_at");

            // ── 2. stock_logs(created_at) ─────────────────────────────────────
            // EF Core snake_case convention: CreatedAt → created_at
            // Fire raporu: WHERE created_at >= @from AND created_at < @to
            migrationBuilder.CreateIndex(
                name: "ix_stock_logs_createdat",
                table: "stock_logs",
                column: "created_at");

            // ── 3. order_items(menu_item_id) — top ürün raporu ────────────────
            // EF Core snake_case convention: MenuItemId → menu_item_id
            // "En çok satan ürünler": GROUP BY menu_item_id, COUNT(*) DESC
            // ix_order_items_orderid var ama bu MenuItemId bazlı GROUP BY için ayrı.
            migrationBuilder.CreateIndex(
                name: "ix_order_items_menuitemid",
                table: "order_items",
                column: "menu_item_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_orders_closedat",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_stock_logs_createdat",
                table: "stock_logs");

            migrationBuilder.DropIndex(
                name: "ix_order_items_menuitemid",
                table: "order_items");
        }
    }
}
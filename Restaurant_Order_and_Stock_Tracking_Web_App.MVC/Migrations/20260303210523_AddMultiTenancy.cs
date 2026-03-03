using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "uq_categories_name",
                table: "categories");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "tables",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "menu_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "categories",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TenantId",
                table: "AspNetUsers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Subdomain = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PlanType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "trial"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    TrialEndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RestaurantType = table.Column<int>(type: "integer", nullable: false, defaultValue: 1)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "tenant_configs",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EnableTableMerge = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    EnableKitchenDisplay = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    EnableSelfOrderQr = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    EnableCourseManagement = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    EnableReservations = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    EnableSplitBill = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    EnableDiscounts = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    TaxRate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    CurrencyCode = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "TRY"),
                    EnableLoyaltyProgram = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    EnableMultiBranch = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LogoPath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PrimaryColor = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    RestaurantDisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_configs", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_tenant_configs_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tables_TenantId",
                table: "tables",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_orders_TenantId",
                table: "orders",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_menu_items_TenantId",
                table: "menu_items",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "uq_categories_tenant_name",
                table: "categories",
                columns: new[] { "TenantId", "CategoryName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "uq_tenants_subdomain",
                table: "tenants",
                column: "Subdomain",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_tenants_TenantId",
                table: "AspNetUsers",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "TenantId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_categories_tenants_TenantId",
                table: "categories",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "TenantId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_menu_items_tenants_TenantId",
                table: "menu_items",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "TenantId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_orders_tenants_TenantId",
                table: "orders",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "TenantId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_tables_tenants_TenantId",
                table: "tables",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "TenantId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_tenants_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_categories_tenants_TenantId",
                table: "categories");

            migrationBuilder.DropForeignKey(
                name: "FK_menu_items_tenants_TenantId",
                table: "menu_items");

            migrationBuilder.DropForeignKey(
                name: "FK_orders_tenants_TenantId",
                table: "orders");

            migrationBuilder.DropForeignKey(
                name: "FK_tables_tenants_TenantId",
                table: "tables");

            migrationBuilder.DropTable(
                name: "tenant_configs");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropIndex(
                name: "IX_tables_TenantId",
                table: "tables");

            migrationBuilder.DropIndex(
                name: "IX_orders_TenantId",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "IX_menu_items_TenantId",
                table: "menu_items");

            migrationBuilder.DropIndex(
                name: "uq_categories_tenant_name",
                table: "categories");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "tables");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "menu_items");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "categories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "uq_categories_name",
                table: "categories",
                column: "CategoryName",
                unique: true);
        }
    }
}

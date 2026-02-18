using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Restaurant_Order_and_Stock_Tracking_Web_App.MVC.Migrations
{
    /// <inheritdoc />
    public partial class InitializeMG1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tables",
                columns: table => new
                {
                    TableId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TableName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TableCapacity = table.Column<int>(type: "integer", maxLength: 2, nullable: false, defaultValue: 4),
                    TableStatus = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TableCreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tables", x => x.TableId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tables");
        }
    }
}

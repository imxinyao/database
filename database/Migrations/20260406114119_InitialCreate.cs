using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace database.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "operator_info",
                columns: table => new
                {
                    operator_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    operator_code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    operator_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    contact_person = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                    contact_phone = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_operator_info", x => x.operator_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "station_info",
                columns: table => new
                {
                    station_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    station_code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    station_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    is_transfer = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_station_info", x => x.station_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ticket_transaction",
                columns: table => new
                {
                    transaction_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    card_no = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    entry_time = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    entry_station_id = table.Column<long>(type: "bigint", nullable: true),
                    exit_time = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    exit_station_id = table.Column<long>(type: "bigint", nullable: true),
                    pay_amount = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    payment_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    transaction_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    transaction_status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                    exception_type = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_transaction", x => x.transaction_id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "line_info",
                columns: table => new
                {
                    line_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    line_code = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    line_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    operator_id = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_line_info", x => x.line_id);
                    table.ForeignKey(
                        name: "FK_line_info_operator_info_operator_id",
                        column: x => x.operator_id,
                        principalTable: "operator_info",
                        principalColumn: "operator_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "section_info",
                columns: table => new
                {
                    section_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    line_id = table.Column<long>(type: "bigint", nullable: false),
                    from_station_id = table.Column<long>(type: "bigint", nullable: false),
                    to_station_id = table.Column<long>(type: "bigint", nullable: false),
                    distance_km = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    is_bidirectional = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_section_info", x => x.section_id);
                    table.ForeignKey(
                        name: "FK_section_info_line_info_line_id",
                        column: x => x.line_id,
                        principalTable: "line_info",
                        principalColumn: "line_id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_line_info_operator_id",
                table: "line_info",
                column: "operator_id");

            migrationBuilder.CreateIndex(
                name: "IX_section_info_line_id",
                table: "section_info",
                column: "line_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "section_info");

            migrationBuilder.DropTable(
                name: "station_info");

            migrationBuilder.DropTable(
                name: "ticket_transaction");

            migrationBuilder.DropTable(
                name: "line_info");

            migrationBuilder.DropTable(
                name: "operator_info");
        }
    }
}

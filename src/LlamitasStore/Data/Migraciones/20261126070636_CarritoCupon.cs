using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LlamitasStore.Data.Migraciones
{
    /// <inheritdoc />
    public partial class CarritoCupon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "codigo_cupon",
                table: "carrito",
                type: "varchar(40)",
                maxLength: 40,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "codigo_cupon",
                table: "carrito");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MatDo.Data.Migrations
{
    /// <inheritdoc />
    public partial class MatIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdentitySessionId",
                table: "UserSession",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentityToken",
                table: "UserSession",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSubject",
                table: "User",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSession_IdentitySessionId",
                table: "UserSession",
                column: "IdentitySessionId");

            migrationBuilder.CreateIndex(
                name: "IX_User_ExternalSubject",
                table: "User",
                column: "ExternalSubject",
                unique: true,
                filter: "\"ExternalSubject\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserSession_IdentitySessionId",
                table: "UserSession");

            migrationBuilder.DropIndex(
                name: "IX_User_ExternalSubject",
                table: "User");

            migrationBuilder.DropColumn(
                name: "IdentitySessionId",
                table: "UserSession");

            migrationBuilder.DropColumn(
                name: "IdentityToken",
                table: "UserSession");

            migrationBuilder.DropColumn(
                name: "ExternalSubject",
                table: "User");
        }
    }
}

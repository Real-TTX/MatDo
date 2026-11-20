using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MatDo.Data.Migrations
{
    /// <inheritdoc />
    public partial class TaskCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskCompletion",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TaskId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    AssigneeId = table.Column<long>(type: "bigint", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    NextDue = table.Column<DateOnly>(type: "date", nullable: true),
                    CreateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreateUserId = table.Column<long>(type: "bigint", nullable: true),
                    UpdateDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskCompletion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskCompletion_Task_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Task",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskCompletion_User_AssigneeId",
                        column: x => x.AssigneeId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TaskCompletion_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskCompletion_AssigneeId",
                table: "TaskCompletion",
                column: "AssigneeId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskCompletion_TaskId_CompletedAt",
                table: "TaskCompletion",
                columns: new[] { "TaskId", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskCompletion_UserId",
                table: "TaskCompletion",
                column: "UserId");

            // the tasks that were completed before there was a history: one check-off each, by the last person who changed them (when that account still exists)
            migrationBuilder.Sql("""
                INSERT INTO "TaskCompletion" ("TaskId", "UserId", "AssigneeId", "CompletedAt", "DueDate", "NextDue", "CreateDate", "CreateUserId", "UpdateDate", "UpdateUserId")
                SELECT t."Id",
                       u."Id",
                       t."AssigneeId",
                       COALESCE(t."CompletedDate", t."UpdateDate"),
                       t."DueDate",
                       NULL,
                       COALESCE(t."CompletedDate", t."UpdateDate"),
                       u."Id",
                       COALESCE(t."CompletedDate", t."UpdateDate"),
                       u."Id"
                FROM "Task" t
                LEFT JOIN "User" u ON u."Id" = COALESCE(t."UpdateUserId", t."CreateUserId")
                WHERE t."IsCompleted" AND t."UpdateState" <> 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskCompletion");
        }
    }
}

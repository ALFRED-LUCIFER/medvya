using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Platform.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// MDV-P5-040: the platform console's team, security keys, key links, sessions and the append-only platform audit log. Forward-only: no Down.
    /// </summary>
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "platform");

            migrationBuilder.CreateTable(
                name: "AuditEntries",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorMemberId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubjectTenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClientIp = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Members",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    Role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Issuer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Subject = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    InvitedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    InvitedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    AccessEndsOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LastActiveAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RemovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Members", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KeyLinks",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    IssuedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KeyLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KeyLinks_Members_MemberId",
                        column: x => x.MemberId,
                        principalSchema: "platform",
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SecurityKeys",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialId = table.Column<byte[]>(type: "bytea", maxLength: 1023, nullable: false),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    SignCount = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityKeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecurityKeys_Members_MemberId",
                        column: x => x.MemberId,
                        principalSchema: "platform",
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                schema: "platform",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MemberId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    KeyVerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    KeyLinkId = table.Column<Guid>(type: "uuid", nullable: true),
                    PendingCeremony = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sessions_Members_MemberId",
                        column: x => x.MemberId,
                        principalSchema: "platform",
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEntries_OccurredAt",
                schema: "platform",
                table: "AuditEntries",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_KeyLinks_MemberId",
                schema: "platform",
                table: "KeyLinks",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_KeyLinks_TokenHash",
                schema: "platform",
                table: "KeyLinks",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Members_Issuer_Subject",
                schema: "platform",
                table: "Members",
                columns: new[] { "Issuer", "Subject" },
                unique: true,
                filter: "\"Subject\" IS NOT NULL AND \"RemovedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityKeys_CredentialId",
                schema: "platform",
                table: "SecurityKeys",
                column: "CredentialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityKeys_MemberId",
                schema: "platform",
                table: "SecurityKeys",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_MemberId",
                schema: "platform",
                table: "Sessions",
                column: "MemberId");

            // Append-only for every role, the owner included, and ENABLE ALWAYS so session_replication_role = replica does not skip it.
            // shortcut: not hash-chained like the clinic ledger (the clinic digest job must not read console data); add a chain and digests
            // when the control plane moves to its own database (MDV-P5-017).
            migrationBuilder.Sql("""
                CREATE FUNCTION platform.refuse() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION '% is append only: % is refused', TG_TABLE_NAME, TG_OP USING ERRCODE = 'restrict_violation';
                END $$;
                CREATE TRIGGER "TR_AuditEntries_AppendOnly" BEFORE UPDATE OR DELETE ON platform."AuditEntries" FOR EACH ROW EXECUTE FUNCTION platform.refuse();
                CREATE TRIGGER "TR_AuditEntries_NoTruncate" BEFORE TRUNCATE ON platform."AuditEntries" FOR EACH STATEMENT EXECUTE FUNCTION platform.refuse();
                ALTER TABLE platform."AuditEntries" ENABLE ALWAYS TRIGGER "TR_AuditEntries_AppendOnly", ENABLE ALWAYS TRIGGER "TR_AuditEntries_NoTruncate";
                """);
        }
    }
}

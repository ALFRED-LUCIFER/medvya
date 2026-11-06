using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Tenancy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Baseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tenancy");

            migrationBuilder.CreateTable(
                name: "AccessReviewDecisions",
                schema: "tenancy",
                columns: table => new
                {
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Decision = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DecidedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessReviewDecisions", x => new { x.ReviewId, x.MembershipId });
                });

            migrationBuilder.CreateTable(
                name: "TenantIndex",
                schema: "tenancy",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantIndex", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "Tenants",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CountryCode = table.Column<string>(type: "character(2)", fixedLength: true, maxLength: 2, nullable: false),
                    ResidencyRegion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IdleTimeoutMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 15)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                    table.CheckConstraint("CK_Tenants_IdleTimeoutMinutes", "\"IdleTimeoutMinutes\" BETWEEN 1 AND 480");
                });

            migrationBuilder.CreateTable(
                name: "AccessReviews",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quarter = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccessReviews_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomRoles",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    BaseRole = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Capabilities = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomRoles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Facilities",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Facilities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Facilities_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FeatureFlags",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Owner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureFlags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeatureFlags_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PracticeDefaults",
                schema: "tenancy",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Specialty = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Qualifications = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Languages = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SlotMinutes = table.Column<int>(type: "integer", nullable: false),
                    NewVisitFeeMinor = table.Column<long>(type: "bigint", nullable: false),
                    FollowUpFeeMinor = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    WeekdayHours = table.Column<string>(type: "text", nullable: false),
                    SaturdayHours = table.Column<string>(type: "text", nullable: false),
                    NoteTemplates = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PracticeDefaults", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_PracticeDefaults_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Profiles",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RegistrationNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TaxNumber = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    LogoKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Profiles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SettingsChanges",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Section = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Before = table.Column<string>(type: "text", nullable: true),
                    After = table.Column<string>(type: "text", nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettingsChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettingsChanges_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TermsAcceptances",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TermsVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AcceptedBy = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TermsAcceptances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TermsAcceptances_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalSchema: "tenancy",
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Closures",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Closures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Closures_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalSchema: "tenancy",
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Memberships",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CustomRoleId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Memberships", x => x.Id);
                    table.CheckConstraint("CK_Memberships_UserUnlessInvited", "\"Status\" = 'Invited' OR \"UserId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_Memberships_CustomRoles_CustomRoleId",
                        column: x => x.CustomRoleId,
                        principalSchema: "tenancy",
                        principalTable: "CustomRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Memberships_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalSchema: "tenancy",
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OperatingHours",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FacilityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Weekday = table.Column<int>(type: "integer", nullable: false),
                    Opens = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Closes = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OperatingHours", x => x.Id);
                    table.CheckConstraint("CK_OperatingHours_OpensBeforeCloses", "\"Opens\" < \"Closes\"");
                    table.ForeignKey(
                        name: "FK_OperatingHours_Facilities_FacilityId",
                        column: x => x.FacilityId,
                        principalSchema: "tenancy",
                        principalTable: "Facilities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DoctorLicences",
                schema: "tenancy",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Authority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ExpiresOn = table.Column<DateOnly>(type: "date", nullable: true),
                    CertificateName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerifiedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorLicences", x => x.MembershipId);
                    table.ForeignKey(
                        name: "FK_DoctorLicences_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalSchema: "tenancy",
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DoctorProfiles",
                schema: "tenancy",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Specialty = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Qualifications = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Languages = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    NewVisitFeeMinor = table.Column<long>(type: "bigint", nullable: false),
                    FollowUpFeeMinor = table.Column<long>(type: "bigint", nullable: false),
                    SignatureKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorProfiles", x => x.MembershipId);
                    table.ForeignKey(
                        name: "FK_DoctorProfiles_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalSchema: "tenancy",
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Invitations",
                schema: "tenancy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", fixedLength: true, maxLength: 32, nullable: false),
                    InvitedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invitations_Memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalSchema: "tenancy",
                        principalTable: "Memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessReviews_TenantId_Quarter",
                schema: "tenancy",
                table: "AccessReviews",
                columns: new[] { "TenantId", "Quarter" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Closures_FacilityId_Date",
                schema: "tenancy",
                table: "Closures",
                columns: new[] { "FacilityId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomRoles_TenantId_Name",
                schema: "tenancy",
                table: "CustomRoles",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Facilities_TenantId",
                schema: "tenancy",
                table: "Facilities",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureFlags_TenantId_Key",
                schema: "tenancy",
                table: "FeatureFlags",
                columns: new[] { "TenantId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_MembershipId",
                schema: "tenancy",
                table: "Invitations",
                column: "MembershipId");

            migrationBuilder.CreateIndex(
                name: "IX_Invitations_TokenHash",
                schema: "tenancy",
                table: "Invitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_CustomRoleId",
                schema: "tenancy",
                table: "Memberships",
                column: "CustomRoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_FacilityId",
                schema: "tenancy",
                table: "Memberships",
                column: "FacilityId");

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_TenantId_UserId_FacilityId",
                schema: "tenancy",
                table: "Memberships",
                columns: new[] { "TenantId", "UserId", "FacilityId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Memberships_UserId",
                schema: "tenancy",
                table: "Memberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OperatingHours_FacilityId_Weekday",
                schema: "tenancy",
                table: "OperatingHours",
                columns: new[] { "FacilityId", "Weekday" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Profiles_TenantId",
                schema: "tenancy",
                table: "Profiles",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettingsChanges_TenantId_OccurredAt",
                schema: "tenancy",
                table: "SettingsChanges",
                columns: new[] { "TenantId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TermsAcceptances_TenantId_TermsVersion",
                schema: "tenancy",
                table: "TermsAcceptances",
                columns: new[] { "TenantId", "TermsVersion" });
            // The facility picker finds tenants through this index (tenant ids only), kept in step with the tenants by trigger.
            migrationBuilder.Sql("""
                CREATE FUNCTION tenancy."Tenants_Index"() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    INSERT INTO tenancy."TenantIndex" ("TenantId") VALUES (NEW."Id") ON CONFLICT DO NOTHING;
                    RETURN NULL;
                END $$;
                CREATE TRIGGER "TR_Tenants_Index" AFTER INSERT ON tenancy."Tenants"
                    FOR EACH ROW EXECUTE FUNCTION tenancy."Tenants_Index"();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER "TR_Tenants_Index" ON tenancy."Tenants";
                DROP FUNCTION tenancy."Tenants_Index"();
                """);
            migrationBuilder.DropTable(
                name: "AccessReviewDecisions",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "AccessReviews",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Closures",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "DoctorLicences",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "DoctorProfiles",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "FeatureFlags",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Invitations",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "OperatingHours",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "PracticeDefaults",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Profiles",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "SettingsChanges",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "TenantIndex",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "TermsAcceptances",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Memberships",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "CustomRoles",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Facilities",
                schema: "tenancy");

            migrationBuilder.DropTable(
                name: "Tenants",
                schema: "tenancy");
        }
    }
}

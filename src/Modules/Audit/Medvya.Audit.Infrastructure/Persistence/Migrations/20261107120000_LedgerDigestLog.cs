using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Medvya.Audit.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// MDV-P0-042: the database records each digest it published (name and time, no tenant data). Verification can then tell "no digest yet"
    /// (a new environment, a warning) from "the digests are gone" (an alert). It is append-only like the ledger tables. Forward-only: no Down.
    /// </summary>
    [DbContext(typeof(AuditDbContext))]
    [Migration("20261107120000_LedgerDigestLog")]
    public partial class LedgerDigestLog : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE ledger."Digests" ("Name" text PRIMARY KEY, "At" timestamptz NOT NULL);
                CREATE TRIGGER "TR_Digests_AppendOnly" BEFORE UPDATE OR DELETE ON ledger."Digests" FOR EACH ROW EXECUTE FUNCTION ledger.refuse();
                CREATE TRIGGER "TR_Digests_NoTruncate" BEFORE TRUNCATE ON ledger."Digests" FOR EACH STATEMENT EXECUTE FUNCTION ledger.refuse();
                ALTER TABLE ledger."Digests" ENABLE ALWAYS TRIGGER "TR_Digests_AppendOnly", ENABLE ALWAYS TRIGGER "TR_Digests_NoTruncate";
                """);
        }
    }
}

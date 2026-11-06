using Medvya.DbMigrate;
using Medvya.Tenancy.Domain;
using Npgsql;

namespace Medvya.IntegrationTests;

/// <summary>MDV-P0-019: the restore drill compares tables by row count; the counts must be right and stable across a restore.</summary>
public sealed class RestoreDrillCountsTests(PostgresFixture sql) : IClassFixture<PostgresFixture>
{
    async Task<IReadOnlyList<(string Table, long Rows)>> Counts()
    {
        await using var admin = new NpgsqlConnection(sql.ConnectionString);
        await admin.OpenAsync();
        return await DatabaseMigrator.RowCountsAsync(admin);
    }

    [Fact]
    public async Task ATableWithSecondaryIndexesIsCountedOncePerRow()
    {
        // Every index has its own partition row count; counting them all would report each row once per index.
        await using (var admin = new NpgsqlConnection(sql.ConnectionString))
        {
            await admin.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                CREATE TABLE public."DrillProbe" ("Id" int NOT NULL PRIMARY KEY, "A" int NOT NULL, "B" int NOT NULL);
                CREATE INDEX "IX_DrillProbe_A" ON public."DrillProbe" ("A");
                CREATE INDEX "IX_DrillProbe_B" ON public."DrillProbe" ("B");
                INSERT INTO public."DrillProbe" VALUES (1, 1, 1), (2, 2, 2), (3, 3, 3);
                """, admin);
            await cmd.ExecuteNonQueryAsync();
        }
        try
        {
            Assert.Equal(3, (await Counts()).Single(c => c.Table == "public.DrillProbe").Rows);
        }
        finally
        {
            await using var admin = new NpgsqlConnection(sql.ConnectionString);
            await admin.OpenAsync();
            await using var cmd = new NpgsqlCommand("DROP TABLE public.\"DrillProbe\"", admin);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task CountsGrowByExactlyTheRowsWritten()
    {
        var before = (await Counts()).ToDictionary(c => c.Table, c => c.Rows);
        var tenant = Guid.NewGuid();
        await using (var db = sql.Context(tenant))
        {
            db.Tenants.Add(new Tenant { Id = tenant, Name = "Restore Drill (synthetic)", CountryCode = "IN", ResidencyRegion = "centralindia" });
            await db.SaveChangesAsync();
        }

        var after = await Counts();
        Assert.Equal(before["tenancy.Tenants"] + 1, after.Single(c => c.Table == "tenancy.Tenants").Rows);
        Assert.Contains(after, c => c.Table.EndsWith(".__EFMigrationsHistory", StringComparison.Ordinal) && c.Rows > 0);
        Assert.Equal(after.Select(c => c.Table).Order(StringComparer.Ordinal), after.Select(c => c.Table)); // stable order, so two runs diff cleanly
    }
}

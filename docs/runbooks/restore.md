# Restore runbook: database and blob (MDV-P0-019)

> The database is moving from Azure SQL to PostgreSQL (ADR-012). The database steps below apply until the Azure SQL server is retired; the PostgreSQL drill is `postgres-restore-drill.md` (MDV-P0-038). The blob steps still apply.

Who: the Entra SQL administrator (a human, with just-in-time elevation in production, MDV-P0-025). The app and the deploy identity cannot restore.
Why this exists: P0 exit gate 2 needs a restore that was run, timed and checked, not a belief that backups work.

## What is protected (all in `infra/bicep/main.bicep`, none set by hand)

| What | Setting | Dev | Production |
|---|---|---|---|
| Point-in-time restore (PITR) of `medvya` | `pitrRetentionDays` | 7 days | **35 days** |
| Long-term retention of weekly full backups | `ltrWeeklyRetention` | **off** (Azure refuses it on a serverless database that auto-pauses) | per country rule, on an always-on tier, before real patient data |
| Where backups are stored | `backupRedundancy` | `Local` | `Local` or `Zone`, **never `Geo`** (no copy outside the region, AGENTS.md 1.6) |
| Blob images (doctor signatures, logos) | soft delete 7 days, container soft delete 7 days, versioning on | yes | yes |

Backups are encrypted with the same transparent data encryption as the database. The Always Encrypted column master key lives only in Key Vault (`kv-…`, purge protection on): a restored database reads encrypted columns only for identities allowed on that key.

## Restore the database to a point in time (creates a NEW database)

A restore never overwrites `medvya`. Restore to a new name, check it, then (only if you are recovering for real) switch the app by changing the `ConnectionStrings__Medvya` database name, or copy back what you need.

1. Choose the instant `T` (UTC): not earlier than `az sql db show … --query earliestRestoreDate`, and at least 10 minutes in the past (log backups lag).
2. Open the firewall for your own address only, and remove it when done:
   `az sql server firewall-rule create -g rg-medvya-dev-in -s <server> -n restore-me --start-ip-address <ip> --end-ip-address <ip>`
3. Restore, timed (note the clock before and after):
   `date -u; az sql db restore -g rg-medvya-dev-in -s <server> -n medvya --dest-name medvya-restore-drill --time <T> --service-objective GP_S_Gen5_1; date -u`
4. Wait until `az sql db show … -n medvya-restore-drill --query status` is `Online` (the serverless database may resume from pause: allow a minute).

## Smoke test: row counts per table match the instant `T`

Counts come from metadata only (no row is read, no patient data is shown):

```
dotnet run --project tools/db-migrate -- counts "Server=tcp:<server>.database.windows.net,1433;Database=medvya;Authentication=Active Directory Default;Encrypt=Strict;" > source.txt
dotnet run --project tools/db-migrate -- counts "Server=tcp:<server>.database.windows.net,1433;Database=medvya-restore-drill;Authentication=Active Directory Default;Encrypt=Strict;" > restored.txt
diff source.txt restored.txt && echo "row counts match"
```

For the two files to match, nothing may have been written to `medvya` between `T` and the moment you ran the first command. In dev, take the source counts first, wait 10 minutes without using the app, then restore to a time one minute after the counts were taken. In production use a quiet window or compare against counts recorded at `T`.

Also open the restored database once with a read of the migration history and the key metadata:
`SELECT COUNT(*) FROM dbo.__EFMigrationsHistory; SELECT name FROM sys.column_encryption_keys;` (the key `CEK_Patients` must be listed).

## Clean up (always)

`az sql db delete -g rg-medvya-dev-in -s <server> -n medvya-restore-drill --yes` and `az sql server firewall-rule delete -g rg-medvya-dev-in -s <server> -n restore-me`.

## Other recoveries

- A deleted database: `az sql db restore … --deleted-time <t>` from the deleted-databases list (within the PITR window).
- A deleted or overwritten blob (signature, logo): with soft delete, undelete it (`az storage blob undelete`); with versioning, copy the previous version back. Both keep 7 days.
- Backups older than the PITR window: long-term retention weekly backups (`az sql db ltr-backup list`), restored the same way. Not available in dev (see the table above).

## Drill record (P0 exit gate 2)

| Drill | Date (UTC) | Restore point `T` | Database size | Restore started | Online | Elapsed | Row counts | Done by | Notes |
|---|---|---|---|---|---|---|---|---|---|
| 1 | _pending: first drill after this PR is deployed_ | | | | | | | | |

Targets until measured: the gate only needs the time recorded. Write the measured elapsed time here and in the R0 release notes (MDV-P0-029); revisit the target when the database holds real volume.

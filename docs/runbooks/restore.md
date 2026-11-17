# Restore runbook: database and blob (MDV-P0-019)

Who: the Entra administrator of the PostgreSQL server or storage account (a human, with just-in-time elevation in production, MDV-P0-025). The app and the deploy identity cannot restore.
Why this exists: P0 exit gate 2 needs a restore that was run, timed and checked, not a belief that backups work.

## What is protected (all in `infra/bicep`, none set by hand)

| What | Setting | Dev | Production |
|---|---|---|---|
| Point-in-time restore (PITR) of the PostgreSQL server | `pitrRetentionDays` | 7 days | **35 days** |
| Geo-redundant backup | `geoRedundantBackup` | off | off, unless the paired region is in the same country (AGENTS.md 1.6) |
| Blob images (doctor signatures, logos) | soft delete 7 days, container soft delete 7 days, versioning on | yes | yes |
| Ledger digests (Blob, immutable) | `ledgerDigestRetentionDays` | 7 days | 3,650 days |

Backups are encrypted at rest with the server's customer-managed key in Key Vault (purge protection on). National IDs are also encrypted by the application (per-tenant keys wrapped by the Key Vault key `medvya-national-id`, ADR-012), so a restored database is unreadable for those columns without that key.

## Restore the database

Follow [postgres-restore-drill.md](postgres-restore-drill.md). A restore always creates a new server in the same region and country; it never overwrites the live one. To recover for real, switch the app's connection string to the restored server only after the checks in that runbook pass.

## Other recoveries

- A deleted or overwritten blob (signature, logo): with soft delete, undelete it (`az storage blob undelete`); with versioning, copy the previous version back. Both keep 7 days.

## Drill record (P0 exit gate 2)

| Drill | Date (UTC) | Restore point `T` | Database size | Restore started | Ready | Elapsed | Row counts | Done by | Notes |
|---|---|---|---|---|---|---|---|---|---|
| 1 | _pending_ | | | | | | | | |

Targets until measured: the gate only needs the time recorded. Write the measured elapsed time here and in the R0 release notes (MDV-P0-029); revisit the target when the database holds real volume.

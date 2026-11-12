# PostgreSQL point-in-time restore drill (MDV-P0-038)

Who: the Entra administrator of the server (a human; just-in-time elevation in production, MDV-P0-025). The app and the deploy identity cannot restore.
Why: P0 exit gate 2 needs a restore that was run and timed, not a belief that backups work. This replaces the SQL steps in `restore.md` (MDV-P0-019) for the database; the blob steps there still apply.

## What is protected (all in `infra/bicep/postgres.bicep`, none set by hand)

| What | Setting | Dev | Production |
|---|---|---|---|
| Point-in-time restore window | `pitrRetentionDays` | 7 days | **14 days** |
| Zone-redundant HA | `postgresHaEnabled` | off | on |
| Geo-redundant backup | `geoRedundantBackup` | off | off, unless the Azure paired region is in the same country (India: Central to South; UAE: North to Central). The module fails the deployment otherwise |

A restore always creates a NEW server in the same region and the same country. Never restore to another country (AGENTS.md 1.6). The new server keeps the private-only, Entra-only settings from the source but you must check that (step 5).

## Drill steps

Replace `<rg>`, `<server>` (output `postgresServer`) and `<T>` (UTC, ISO 8601, at least 10 minutes in the past and not before the earliest restore point).

1. Earliest restore point: `az postgres flexible-server show -g <rg> -n <server> --query backup.earliestRestoreDate -o tsv`.
2. Record source facts before starting: database size and the row counts per table (`dotnet run --project tools/db-migrate -- counts <connection>`; metadata only, no patient data is shown). Quiet window: nothing writes between `T` and the count.
3. Restore into a new server, timed (note the clock before and after):
   `date -u; az postgres flexible-server restore -g <rg> -n <server>-drill --source-server <server> --restore-time <T> --vnet <vnet> --subnet snet-postgres --private-dns-zone <zone id>; date -u`
   The restore must land in the same VNet and private DNS zone so it has no public endpoint. If the subnet is already taken by the source server, use a second delegated subnet in the same VNet.
4. Wait until `az postgres flexible-server show -g <rg> -n <server>-drill --query state -o tsv` is `Ready`. Record the time.
5. Check the restored server: `network.publicNetworkAccess` is `Disabled`, `authConfig.passwordAuth` is `Disabled`, `dataEncryption.type` is `AzureKeyVault`, location is in the same country. Re-add the Entra administrator if the restore did not carry it over.
6. Smoke test from inside the VNet as the administrator: row counts per table match step 2, and `SELECT COUNT(*) FROM <module schema>."__EFMigrationsHistory"` for each module is the same as the source.
7. Clean up (always): `az postgres flexible-server delete -g <rg> -n <server>-drill --yes`.

## What to record

Source and restored server names, region, SKU, database size, restore point `T`, restore started, state `Ready`, elapsed time, row-count result, results of the step 5 checks, who ran it, and anything odd.

## Pass criteria

- The restore completes and the server is `Ready` within the agreed time: **TBD** (set from the first drill, then reviewed when the database holds real volume).
- Step 5 checks all pass: no public endpoint, no password login, CMK in use, same country.
- Row counts match the instant `T`.
- Clean-up done.

## Drill record (P0 exit gate 2)

| Drill | Date (UTC) | Restore point `T` | Database size | Started | Ready | Elapsed | Row counts | Step 5 checks | Done by | Notes |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | 2026-11-12, three attempts, **all failed** | 18:52 to 18:53 | 32 GB (B1ms) | 19:07 to 19:13 | never | n/a | n/a | n/a | assistant with `az`, founder's account | Azure refused each restore with `SkuNotAvailable` (same VNet and subnet, with and without `--zone 2`, with the CMK key and identity) although `list-skus` shows Standard_B1ms in zones 1 to 3. The source server was untouched; the half-created drill server was removed each time. Next: open an Azure support request for new-server capacity in Central India, or retry with `--sku-name Standard_B2s`; add a `--backup-identity` (a second identity) only if geo backup is turned on |

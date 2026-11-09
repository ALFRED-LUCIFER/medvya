"""Generates docs/03-architecture/erd.md from the EF Core model snapshots, the module project references and docs/03-architecture/data-model.md.

    python3 -I tools/erd/generate.py           # rewrite the file
    python3 -I tools/erd/generate.py --check   # CI: exit 1 when the file is stale

Built tables are exact (read from the snapshots). Cross-module links are NOT database foreign keys (AGENTS.md 3.1: a module never touches another
module's tables); they are listed by column name in SOFT_REFS below and drawn dotted. Nothing here is time- or commit-dependent, so the output is stable.
"""
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from parse_snapshots import parse_all  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'docs/03-architecture/erd.md'

# column name -> (schema, table) it refers to by id, with no database foreign key. Names not listed are not drawn.
SOFT_REFS = {
    'TenantId': ('tenancy', 'Tenants'), 'FacilityId': ('tenancy', 'Facilities'), 'MembershipId': ('tenancy', 'Memberships'),
    'CustomRoleId': ('tenancy', 'CustomRoles'), 'PatientId': ('patients', 'Patients'), 'CandidatePatientId': ('patients', 'Patients'),
    'MergedIntoPatientId': ('patients', 'Patients'), 'MergedFromPatientId': ('patients', 'Patients'), 'MovedFromPatientId': ('patients', 'Patients'),
    'UserId': ('identity', 'Users'), 'ActorUserId': ('identity', 'Users'), 'RegisteredBy': ('identity', 'Users'), 'ChangedBy': ('identity', 'Users'),
    'BookedBy': ('identity', 'Users'), 'DecidedBy': ('identity', 'Users'), 'RequestedBy': ('identity', 'Users'), 'LeadClinicianId': ('identity', 'Users'), 'EndedBy': ('identity', 'Users'), 'HeldBy': ('identity', 'Users'), 'VerifiedBy': ('identity', 'Users'), 'RecordedBy': ('identity', 'Users'), 'EscalatedBy': ('identity', 'Users'), 'HandedOffBy': ('identity', 'Users'), 'NoKnownAllergiesBy': ('identity', 'Users'), 'StatusSetBy': ('identity', 'Users'),
    'AppointmentId': ('scheduling', 'Appointments'),
    'AddedBy': ('identity', 'Users'), 'CreatedBy': ('identity', 'Users'),
    'NotificationId': ('notifications', 'Notifications'), 'ReviewId': ('tenancy', 'AccessReviews'),
}
# CalendarId (and the from/to of a reschedule) is a doctor (a membership, inferred) or a room, told apart by CalendarKind.
CALENDAR_COLS = ('CalendarId', 'FromCalendarId', 'ToCalendarId')
CALENDAR = [('tenancy', 'Memberships', 'doctor calendar'), ('scheduling', 'Resources', 'room calendar')]
# planned entity (docs/03-architecture/data-model.md) -> built table
PLANNED_TO_BUILT = {
    'TENANT': 'tenancy.Tenants', 'FACILITY': 'tenancy.Facilities', 'USER': 'identity.Users', 'MEMBERSHIP': 'tenancy.Memberships',
    'ROLE': 'tenancy.CustomRoles', 'CLINICIAN_PROFILE': 'tenancy.DoctorProfiles', 'CLINICIAN_LICENCE': 'tenancy.DoctorLicences',
    'SCHEDULE_TEMPLATE': 'scheduling.Templates', 'LEAVE': 'scheduling.ScheduleExceptions', 'HOLIDAY': 'tenancy.Closures',
    'PATIENT': 'patients.Patients', 'PATIENT_IDENTIFIER': 'patients.Identifiers', 'PATIENT_RELATIONSHIP': 'patients.Relationships',
    'PAYER_COVERAGE': 'patients.PayerCoverages', 'CONSENT_RECEIPT': 'patients.ConsentReceipts', 'SCHEDULE_SLOT': 'scheduling.ScheduleSlots',
    'APPOINTMENT': 'scheduling.Appointments', 'WAITLIST_ENTRY': 'scheduling.Waitlist', 'QUEUE_TOKEN': 'scheduling.QueueTokens', 'DOC_SEQUENCE': 'billing.DocSequences',
    'AUDIT_EVENT': 'audit.AuditEvents', 'OUTBOX_EVENT': 'scheduling.OutboxEvents', 'NOTIFICATION': 'notifications.Notifications',
    'NOTIFICATION_ATTEMPT': 'notifications.Attempts', 'ENCOUNTER': 'encounters.Encounters',
    'OBSERVATION': 'encounters.Observations', 'PROBLEM': 'encounters.Problems', 'ALLERGY': 'encounters.Allergies', 'MEDICATION_STATEMENT': 'encounters.MedicationStatements',
    'CLINICAL_DOCUMENT': 'encounters.ClinicalDocuments',
    'FORMULARY_ITEM': 'prescribing.FormularyItems', 'ORDER_SET': 'prescribing.OrderSets',
}
# where the built table covers only part of the planned entity
PARTIAL = {
    'ROLE': 'custom roles only; the built-in roles are in code (RoleMatrix)',
    'LEAVE': 'also holds extra sessions and shortened days',
    'CONSENT_RECEIPT': 'registration consents; the per-encounter recording consent in area C is not built',
    'NOTIFICATION': 'patient messages only; staff notifications are separate tables',
    'DOC_SEQUENCE': 'numbering only; invoices and payments are not built',
}
LEDGER = {'audit.AuditEvents', 'patients.ConsentReceipts', 'tenancy.TermsAcceptances'}  # hash-chained, MDV-P0-036
MODULE_ORDER = ['Tenancy', 'Identity', 'Patients', 'Scheduling', 'Encounters', 'Prescribing', 'Notifications', 'Audit', 'Leads', 'Billing', 'Platform']


def module_edges():
    """(from module, to module) -> layers referenced, read from the csproj ProjectReference lines."""
    edges: dict[tuple[str, str], set[str]] = {}
    host: set[str] = set()
    for p in (ROOT / 'src').rglob('*.csproj'):
        refs = re.findall(r'ProjectReference Include="[^"]*?([A-Za-z]+)\.([A-Za-z]+)\.([A-Za-z]+)\.csproj"', p.read_text(encoding='utf-8-sig'))
        mine = re.match(r'Medvya\.(\w+)\.(\w+)$', p.stem)
        for _, module, layer in refs:
            if module in ('BuildingBlocks',):
                continue
            if p.stem == 'Host.Api':
                host.add(module)
            elif mine and mine.group(1) != module:
                edges.setdefault((mine.group(1), module), set()).add(layer)
    return edges, host


def planned_entities():
    text = (ROOT / 'docs/03-architecture/data-model.md').read_text(encoding='utf-8')
    body = text.split('## Entities by area', 1)[1].split('## Key constraints', 1)[0]
    out: list[tuple[str, str]] = []
    for area_head, area in re.findall(r'### ([A-D]) · [^\n]*\n(.*?)(?=\n###|\Z)', body, re.S):
        for seg in re.split(r' · ', area.strip()):
            m = re.match(r'([A-Z][A-Z0-9_]*(?: / [A-Z][A-Z0-9_]*)*)', seg.strip())
            if m:
                out += [(n, area_head) for n in m.group(1).split(' / ')]
    return out


def plural(n: int) -> str:
    return f'{n} table' + ('' if n == 1 else 's')


def mm_type(t: str) -> str:
    t = re.sub(r'\(.*?\)', '', t).replace('?', '').strip()
    return re.sub(r'[^A-Za-z0-9_]', '', t) or 'string'


def render() -> str:
    data = parse_all(ROOT)
    tables = {(m['schema'], e['table']): (mod, e) for mod, m in data.items() for e in m['entities'].values()}
    edges, host = module_edges()
    out: list[str] = []
    w = out.append

    w('# Entity-relationship diagram and application map\n')
    w('> **Generated.** Do not edit by hand. Regenerate with `python3 -I tools/erd/generate.py` after any change to an EF Core model or migration, '
      'and commit the result in the same PR. CI runs `python3 -I tools/erd/generate.py --check` and fails when this file is stale (AGENTS.md section 3).\n')
    w('**What is exact and what is inferred**\n')
    w('- *Exact:* every table, column, type, key, nullability, database foreign key and JSON column comes from the EF Core model snapshots '
      '(`src/Modules/*/*/Persistence/Migrations/*ModelSnapshot.cs`), and module dependencies from the `.csproj` references.')
    w('- *Inferred from column names:* the dotted cross-module links (for example `Appointments.PatientId` to `patients.Patients`). They are **not** database '
      'foreign keys: a module never touches another module\'s tables (AGENTS.md 3.1), so the link is an id the application resolves through a service. '
      'The mapping is the `SOFT_REFS` table in `tools/erd/generate.py`.')
    w('- *Planned:* the full-feature model in section 5 is read from `docs/03-architecture/data-model.md`; it is a plan, not a schema.\n')

    # 1 application map
    w('## 1. How the application is linked\n')
    w('One deployable (`Host.Api`) serves the web apps and every module; modules are separate projects that call each other only through application '
      'services and contracts; each module owns one SQL schema in one Azure SQL database per country.\n')
    w('```mermaid\nflowchart LR')
    w('  subgraph Clients')
    w('    web["Clinic web app<br/>React 19, served at /app"]')
    w('    site["Public site<br/>Razor Pages at /"]')
    w('    tv["Waiting-room TV<br/>/queue/tv"]')
    w('  end')
    w('  subgraph Host["Host.Api (one deployable)"]')
    w('    bff["BFF: cookie session,<br/>Entra sign-in with MFA"]')
    w('    api["Endpoints, policies,<br/>rate limits, audit filter"]')
    w('    hub["SignalR board"]')
    w('    jobs["Hangfire jobs<br/>reminders, reviews"]')
    w('  end')
    w('  web --> bff')
    w('  web --> api')
    w('  web -.-> hub')
    w('  tv --> api')
    w('  site --> api')
    for mod in MODULE_ORDER:
        if mod in data:
            w(f'  api --> {mod}["{mod}<br/>schema {data[mod]["schema"]}<br/>{plural(len(data[mod]["entities"]))}"]' if mod in host else f'  {mod}["{mod}<br/>schema {data[mod]["schema"]}<br/>{plural(len(data[mod]["entities"]))} (not wired into the host)"]')
    w('  jobs --> Notifications')
    w('  bff -->|"sign-in"| entra["Microsoft Entra"]')
    w('  Notifications -->|"email"| mail["Azure Communication Services Email<br/>or SMTP"]')
    w('  Host -->|"secrets, column keys"| kv["Key Vault"]')
    w('  Host -->|"telemetry, PHI scrubbed"| ai["Application Insights"]')
    w('  Host ==>|"one database, one schema per module"| sql[("Azure SQL<br/>row-level security on TenantId")]')
    w('```\n')

    # 2 module dependencies
    w('## 2. Which module uses which (project references)\n')
    w('An arrow means the project of the first module references the application, domain or API project of the second. Read from the `.csproj` files.\n')
    w('```mermaid\nflowchart LR')
    for (a, b), layers in sorted(edges.items()):
        w(f'  {a} -->|"{", ".join(sorted(layers))}"| {b}')
    w('```\n')
    w(f'`Host.Api` references: {", ".join(sorted(host))}. Modules with no incoming arrow from another module: ' +
      ', '.join(sorted(m for m in data if not any(b == m for (_, b) in edges))) + '.\n')

    # 3 module-level links by id
    w('## 3. How the data links across modules\n')
    w('Dotted lines are ids that point to another module\'s table without a database foreign key. The label lists up to three columns; `...` means more.\n')
    w('```mermaid\nflowchart LR')
    agg: dict[tuple[str, str], set[str]] = {}
    for (schema, table), (mod, e) in tables.items():
        for col in e['cols']:
            tgt = SOFT_REFS.get(col)
            if col in ('TenantId',):
                continue
            if col in CALENDAR_COLS:
                for ts, tt, _ in CALENDAR:
                    if ts != schema:
                        agg.setdefault((schema, ts), set()).add(col)
                continue
            if tgt and tgt[0] != schema:
                agg.setdefault((schema, tgt[0]), set()).add(col)
    for (a, b), cols in sorted(agg.items()):
        label = ', '.join(sorted(cols)[:3]) + (', ...' if len(cols) > 3 else '')
        w(f'  {a} -.->|"{label}"| {b}')
    w('```\n')
    w('Every tenant-scoped table also carries `TenantId`; it is left out of the picture because it links all modules to `tenancy.Tenants`.\n')

    # 4 per module
    w('## 4. Tables by module\n')
    for mod in MODULE_ORDER:
        if mod not in data:
            continue
        m = data[mod]
        schema = m['schema']
        w(f'### {mod} (schema `{schema}`, {plural(len(m["entities"]))})\n')
        local = {e['name']: e for e in m['entities'].values()}
        w('```mermaid\nerDiagram')
        stubs: set[str] = set()
        rels: list[str] = []
        tbl = {x['table'] for x in m['entities'].values()}
        tname = {x['name']: x['table'] for x in m['entities'].values()}
        for e in m['entities'].values():
            fkcols = {c: tname.get(p, p) for p, c in e['fks']}
            card = lambda col: '||' if e['cols'][col]['required'] else '|o'  # noqa: E731
            w(f'  {e["table"]} {{')
            for col, meta in e['cols'].items():
                if col == 'TenantId' and col not in e['key']:
                    continue  # on nearly every table; the table list below marks the tenant-scoped ones
                if col in e['key'] or col in fkcols or col in SOFT_REFS or col in CALENDAR_COLS:
                    tags = [t for t, on in (('PK', col in e['key']), ('FK', col in fkcols)) if on]
                    note = ''
                    if col in fkcols:
                        note = f' "FK to {fkcols[col]}"'
                    elif col in CALENDAR_COLS:
                        note = ' "doctor or room"'
                    elif col in SOFT_REFS:
                        note = f' "id of {SOFT_REFS[col][0]}.{SOFT_REFS[col][1]}"'
                    w(f'    {mm_type(meta["type"])} {col}{(" " + ", ".join(tags)) if tags else ""}{note}')
            for j in e['json']:
                w(f'    json {j}')
            w('  }')
            for parent, col in e['fks']:
                if parent in tname:
                    rels.append(f'  {tname[parent]} {card(col)}--o{{ {e["table"]} : "{col}"')
            for col in e['cols']:
                if col in ('TenantId', 'Id') or col in fkcols:
                    continue
                if col in CALENDAR_COLS:
                    for ts, tt, label in CALENDAR:
                        stub = tt if ts == schema else f'{ts}_{tt}'
                        if ts != schema:
                            stubs.add(stub)
                        rels.append(f'  {stub} {card(col)}..o{{ {e["table"]} : "{col} ({label})"')
                    continue
                tgt = SOFT_REFS.get(col)
                if not tgt:
                    continue
                if tgt[0] != schema:
                    stub = f'{tgt[0]}_{tgt[1]}'
                    stubs.add(stub)
                    rels.append(f'  {stub} {card(col)}..o{{ {e["table"]} : "{col}"')
                elif tgt[1] in tbl:
                    rels.append(f'  {tgt[1]} {card(col)}..o{{ {e["table"]} : "{col}, no FK"')
        for st in sorted(stubs):
            w(f'  {st} {{\n    guid Id PK "other module"\n  }}')
        for r in dict.fromkeys(rels):
            w(r)
        w('```\n')
        w('| Table | Columns | Primary key | Notes |\n|---|---|---|---|')
        for e in m['entities'].values():
            notes = []
            if f'{schema}.{e["table"]}' in LEDGER:
                notes.append('append-only ledger')
            if 'TenantId' in e['cols']:
                notes.append('tenant-scoped')
            if e['json']:
                notes.append('JSON: ' + ', '.join(e['json']))
            w(f'| `{e["table"]}` | {len(e["cols"])} | {", ".join(e["key"])} | {"; ".join(notes)} |')
        w('')
        for e in m['entities'].values():
            fkcols = {c: p for p, c in e['fks']}
            w(f'<details><summary><code>{schema}.{e["table"]}</code> columns</summary>\n')
            w('| Column | Type | Null | Links to |\n|---|---|---|---|')
            for col, meta in e['cols'].items():
                link = f'FK to `{fkcols[col]}`' if col in fkcols else (f'id of `{SOFT_REFS[col][0]}.{SOFT_REFS[col][1]}`' if col in SOFT_REFS and col not in e['key'] and col != 'TenantId' else ('`tenancy.Tenants`' if col == 'TenantId' else ''))
                if col in CALENDAR_COLS:
                    link = 'doctor or room (`CalendarKind`)'
                w(f'| `{col}` | {meta["type"]} | {"" if meta["required"] else "yes"} | {link} |')
            for j in e['json']:
                w(f'| `{j}` | json | | stored as JSON in this table |')
            w('\n</details>\n')

    # 5 planned
    w('## 5. Whole application: built and planned\n')
    w('From `docs/03-architecture/data-model.md` (areas A to D). "Built" means a table exists in the snapshots; some are narrower than the plan.\n')
    planned = planned_entities()
    bad = [v for v in PLANNED_TO_BUILT.values() if tuple(v.split('.')) not in tables]
    assert not bad, f'PLANNED_TO_BUILT names tables that do not exist: {bad}'
    w('| Area | Planned entity | Status | Built as |\n|---|---|---|---|')
    names = {'A': 'A · Tenant, practice, PMS, billing', 'B': 'B · EMR, clinical authority', 'C': 'C · Consent, capture, AI', 'D': 'D · Care, portal, integration, audit'}
    seen: set[str] = set()
    built = 0
    for n, area in planned:
        first = n not in seen  # a name planned in two areas maps to its first area only
        seen.add(n)
        b = PLANNED_TO_BUILT.get(n) if first else None
        if b:
            built += 1
            note = f' ({PARTIAL[n]})' if n in PARTIAL else ''
            w(f'| {names[area]} | `{n}` | {"partly built" if n in PARTIAL else "built"} | `{b}`{note} |')
        else:
            w(f'| {names[area]} | `{n}` | not built | |')
    mapped = set(PLANNED_TO_BUILT.values())
    extra = sorted(f'{s}.{t}' for (s, t) in tables if f'{s}.{t}' not in mapped)
    w('\nTables that exist but were added while building (not named in the plan): ' + ', '.join(f'`{x}`' for x in extra) + '.\n')
    w(f'**Counts:** {len(tables)} tables across {len(data)} schemas; {built} of {len(planned)} planned entities are built; {len(extra)} tables were added during the build.\n')

    # 6 isolation
    w('## 6. Rules the model follows\n')
    scoped = sorted(f'{s}.{t}' for (s, t), (_, e) in tables.items() if 'TenantId' in e['cols'])
    unscoped = sorted(f'{s}.{t}' for (s, t), (_, e) in tables.items() if 'TenantId' not in e['cols'])
    w(f'- **Tenant isolation:** {len(scoped)} of {len(tables)} tables carry `TenantId` (EF query filter plus PostgreSQL forced row-level security on `medvya.tenant_id`). Without `TenantId`: ' + ', '.join(f'`{x}`' for x in unscoped) + '. (`TenantId` present is not proof the row-level-security policy covers the table; see `docs/03-architecture/database-security.md`.)')
    w('- **Append-only ledgers:** ' + ', '.join(f'`{x}`' for x in sorted(LEDGER)) + ' (hash-chained, MDV-P0-036; each also has ChainSeq, PrevHash and RowHash added by `LedgerSql`, outside the EF model).')
    w('- **No cross-module foreign keys:** modules link by id and through application services (AGENTS.md 3.1).')
    w('- **Country data plane:** one deployment and one database per country (ADR-003); this diagram is the same in each.')
    w('- **Money** is integer minor units plus an ISO currency; **time** is `timestamptz` in UTC (AGENTS.md 3.4, 3.5).\n')
    return '\n'.join(out)


if __name__ == '__main__':
    text = render()
    if '--check' in sys.argv:
        current = OUT.read_text(encoding='utf-8') if OUT.exists() else ''
        if current != text:
            print('docs/03-architecture/erd.md is stale. Run: python3 -I tools/erd/generate.py  and commit the result.', file=sys.stderr)
            sys.exit(1)
        print('erd.md is up to date')
    else:
        OUT.write_text(text, encoding='utf-8')
        print(f'wrote {OUT.relative_to(ROOT)} ({len(text.splitlines())} lines)')

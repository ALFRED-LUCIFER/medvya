# Entity-relationship diagram and application map

> **Generated.** Do not edit by hand. Regenerate with `python3 -I tools/erd/generate.py` after any change to an EF Core model or migration, and commit the result in the same PR. CI runs `python3 -I tools/erd/generate.py --check` and fails when this file is stale (AGENTS.md section 3).

**What is exact and what is inferred**

- *Exact:* every table, column, type, key, nullability, database foreign key and JSON column comes from the EF Core model snapshots (`src/Modules/*/*/Persistence/Migrations/*ModelSnapshot.cs`), and module dependencies from the `.csproj` references.
- *Inferred from column names:* the dotted cross-module links (for example `Appointments.PatientId` to `patients.Patients`). They are **not** database foreign keys: a module never touches another module's tables (AGENTS.md 3.1), so the link is an id the application resolves through a service. The mapping is the `SOFT_REFS` table in `tools/erd/generate.py`.
- *Planned:* the full-feature model in section 5 is read from `docs/03-architecture/data-model.md`; it is a plan, not a schema.

## 1. How the application is linked

One deployable (`Host.Api`) serves the web apps and every module; modules are separate projects that call each other only through application services and contracts; each module owns one SQL schema in one Azure SQL database per country.

```mermaid
flowchart LR
  subgraph Clients
    web["Clinic web app<br/>React 19, served at /app"]
    site["Public site<br/>Razor Pages at /"]
    tv["Waiting-room TV<br/>/queue/tv"]
  end
  subgraph Host["Host.Api (one deployable)"]
    bff["BFF: cookie session,<br/>Entra sign-in with MFA"]
    api["Endpoints, policies,<br/>rate limits, audit filter"]
    hub["SignalR board"]
    jobs["Hangfire jobs<br/>reminders, reviews"]
  end
  web --> bff
  web --> api
  web -.-> hub
  tv --> api
  site --> api
  api --> Tenancy["Tenancy<br/>schema tenancy<br/>20 tables"]
  api --> Identity["Identity<br/>schema identity<br/>3 tables"]
  api --> Patients["Patients<br/>schema patients<br/>7 tables"]
  api --> Scheduling["Scheduling<br/>schema scheduling<br/>15 tables"]
  api --> Encounters["Encounters<br/>schema encounters<br/>13 tables"]
  api --> Prescribing["Prescribing<br/>schema prescribing<br/>5 tables"]
  api --> Notifications["Notifications<br/>schema notifications<br/>5 tables"]
  api --> Audit["Audit<br/>schema audit<br/>1 table"]
  api --> Leads["Leads<br/>schema leads<br/>2 tables"]
  Billing["Billing<br/>schema billing<br/>1 table (not wired into the host)"]
  Platform["Platform<br/>schema platform<br/>5 tables (not wired into the host)"]
  jobs --> Notifications
  bff -->|"sign-in"| entra["Microsoft Entra"]
  Notifications -->|"email"| mail["Azure Communication Services Email<br/>or SMTP"]
  Host -->|"secrets, column keys"| kv["Key Vault"]
  Host -->|"telemetry, PHI scrubbed"| ai["Application Insights"]
  Host ==>|"one database, one schema per module"| sql[("Azure SQL<br/>row-level security on TenantId")]
```

## 2. Which module uses which (project references)

An arrow means the project of the first module references the application, domain or API project of the second. Read from the `.csproj` files.

```mermaid
flowchart LR
  Audit -->|"Application"| Identity
  Audit -->|"Application"| Patients
  Audit -->|"Api"| Tenancy
  Encounters -->|"Api, Application"| Audit
  Encounters -->|"Application"| Identity
  Encounters -->|"Application"| Patients
  Encounters -->|"Application"| Scheduling
  Encounters -->|"Api, Application"| Tenancy
  Identity -->|"Api"| Tenancy
  Notifications -->|"Api, Application"| Audit
  Notifications -->|"Application, Domain"| Patients
  Notifications -->|"Application"| Scheduling
  Notifications -->|"Api, Application, Domain"| Tenancy
  Patients -->|"Api, Application"| Audit
  Patients -->|"Api, Application"| Tenancy
  Prescribing -->|"Api, Application"| Audit
  Prescribing -->|"Application"| Encounters
  Prescribing -->|"Application"| Identity
  Prescribing -->|"Application"| Patients
  Prescribing -->|"Api, Application"| Tenancy
  Scheduling -->|"Api, Application"| Audit
  Scheduling -->|"Application"| Identity
  Scheduling -->|"Application"| Patients
  Scheduling -->|"Api, Application, Domain"| Tenancy
  Tenancy -->|"Application"| Audit
  Tenancy -->|"Application"| Identity
  Tenancy -->|"Application, Domain"| Scheduling
```

`Host.Api` references: Audit, Encounters, Identity, Leads, Notifications, Patients, Prescribing, Scheduling, Tenancy. Modules with no incoming arrow from another module: Billing, Leads, Notifications, Platform, Prescribing.

## 3. How the data links across modules

Dotted lines are ids that point to another module's table without a database foreign key. The label lists up to three columns; `...` means more.

```mermaid
flowchart LR
  audit -.->|"ActorUserId"| identity
  audit -.->|"PatientId"| patients
  audit -.->|"FacilityId"| tenancy
  encounters -.->|"ChangedBy, DecidedBy, EndedBy, ..."| identity
  encounters -.->|"MergedFromPatientId, PatientId"| patients
  encounters -.->|"AppointmentId"| scheduling
  encounters -.->|"FacilityId"| tenancy
  identity -.->|"MembershipId"| tenancy
  notifications -.->|"ChangedBy, UserId"| identity
  notifications -.->|"PatientId"| patients
  notifications -.->|"AppointmentId"| scheduling
  notifications -.->|"FacilityId"| tenancy
  patients -.->|"ChangedBy, DecidedBy, RegisteredBy"| identity
  patients -.->|"FacilityId"| tenancy
  prescribing -.->|"EncounterId"| encounters
  prescribing -.->|"AddedBy, CreatedBy, OrderedBy, ..."| identity
  prescribing -.->|"MergedFromPatientId, PatientId"| patients
  prescribing -.->|"FacilityId"| tenancy
  scheduling -.->|"ActorUserId, BookedBy, ChangedBy, ..."| identity
  scheduling -.->|"MergedFromPatientId, PatientId"| patients
  scheduling -.->|"CalendarId, FacilityId, FromCalendarId, ..."| tenancy
  tenancy -.->|"ActorUserId, ChangedBy, CreatedBy, ..."| identity
  tenancy -.->|"PatientId"| patients
```

Every tenant-scoped table also carries `TenantId`; it is left out of the picture because it links all modules to `tenancy.Tenants`.

## 4. Tables by module

### Tenancy (schema `tenancy`, 20 tables)

```mermaid
erDiagram
  AccessRequests {
    guid Id PK
    guid DecidedBy "id of identity.Users"
    guid FacilityId FK "FK to Facilities"
    guid PatientId "id of patients.Patients"
    guid RequestedBy "id of identity.Users"
  }
  AccessReviews {
    guid Id PK
  }
  AccessReviewDecisions {
    guid ReviewId PK "id of tenancy.AccessReviews"
    guid MembershipId PK "id of tenancy.Memberships"
    guid DecidedBy "id of identity.Users"
  }
  Closures {
    guid Id PK
    guid FacilityId FK "FK to Facilities"
  }
  CustomRoles {
    guid Id PK
  }
  DoctorLicences {
    guid MembershipId PK, FK "FK to Memberships"
  }
  DoctorProfiles {
    guid MembershipId PK, FK "FK to Memberships"
    guid ChangedBy "id of identity.Users"
  }
  Facilities {
    guid Id PK
  }
  FeatureFlags {
    guid Id PK
    guid ChangedBy "id of identity.Users"
    guid CreatedBy "id of identity.Users"
  }
  Invitations {
    guid Id PK
    guid MembershipId FK "FK to Memberships"
  }
  Memberships {
    guid Id PK
    guid CustomRoleId FK "FK to CustomRoles"
    guid FacilityId FK "FK to Facilities"
    guid UserId "id of identity.Users"
  }
  OperatingHours {
    guid Id PK
    guid FacilityId FK "FK to Facilities"
  }
  OrganisationDocuments {
    guid Id PK
  }
  OrganisationRequests {
    guid TenantId PK, FK "FK to Tenants"
  }
  PracticeDefaults {
    guid TenantId PK, FK "FK to Tenants"
  }
  Profiles {
    guid Id PK
    guid ChangedBy "id of identity.Users"
  }
  SettingsChanges {
    guid Id PK
    guid ActorUserId "id of identity.Users"
  }
  Tenants {
    guid Id PK
  }
  TenantIndex {
    guid TenantId PK "id of tenancy.Tenants"
  }
  TermsAcceptances {
    guid Id PK
  }
  identity_Users {
    guid Id PK "other module"
  }
  patients_Patients {
    guid Id PK "other module"
  }
  Facilities ||--o{ AccessRequests : "FacilityId"
  identity_Users |o..o{ AccessRequests : "DecidedBy"
  patients_Patients ||..o{ AccessRequests : "PatientId"
  identity_Users ||..o{ AccessRequests : "RequestedBy"
  Tenants ||--o{ AccessReviews : "TenantId"
  AccessReviews ||..o{ AccessReviewDecisions : "ReviewId, no FK"
  Memberships ||..o{ AccessReviewDecisions : "MembershipId, no FK"
  identity_Users |o..o{ AccessReviewDecisions : "DecidedBy"
  Facilities ||--o{ Closures : "FacilityId"
  Tenants ||--o{ CustomRoles : "TenantId"
  Memberships ||--o{ DoctorLicences : "MembershipId"
  Memberships ||--o{ DoctorProfiles : "MembershipId"
  identity_Users |o..o{ DoctorProfiles : "ChangedBy"
  Tenants ||--o{ Facilities : "TenantId"
  Tenants ||--o{ FeatureFlags : "TenantId"
  identity_Users |o..o{ FeatureFlags : "ChangedBy"
  identity_Users |o..o{ FeatureFlags : "CreatedBy"
  Memberships ||--o{ Invitations : "MembershipId"
  CustomRoles |o--o{ Memberships : "CustomRoleId"
  Facilities ||--o{ Memberships : "FacilityId"
  identity_Users |o..o{ Memberships : "UserId"
  Facilities ||--o{ OperatingHours : "FacilityId"
  Tenants ||--o{ OrganisationDocuments : "TenantId"
  Tenants ||--o{ OrganisationRequests : "TenantId"
  Tenants ||--o{ PracticeDefaults : "TenantId"
  Tenants ||--o{ Profiles : "TenantId"
  identity_Users |o..o{ Profiles : "ChangedBy"
  Tenants ||--o{ SettingsChanges : "TenantId"
  identity_Users |o..o{ SettingsChanges : "ActorUserId"
  Tenants ||--o{ TermsAcceptances : "TenantId"
```

| Table | Columns | Primary key | Notes |
|---|---|---|---|
| `AccessRequests` | 11 | Id | tenant-scoped |
| `AccessReviews` | 5 | Id | tenant-scoped |
| `AccessReviewDecisions` | 6 | ReviewId, MembershipId | tenant-scoped |
| `Closures` | 5 | Id | tenant-scoped |
| `CustomRoles` | 6 | Id | tenant-scoped |
| `DoctorLicences` | 10 | MembershipId | tenant-scoped |
| `DoctorProfiles` | 11 | MembershipId | tenant-scoped |
| `Facilities` | 8 | Id | tenant-scoped |
| `FeatureFlags` | 10 | Id | tenant-scoped |
| `Invitations` | 10 | Id | tenant-scoped |
| `Memberships` | 7 | Id | tenant-scoped |
| `OperatingHours` | 6 | Id | tenant-scoped |
| `OrganisationDocuments` | 8 | Id | tenant-scoped |
| `OrganisationRequests` | 6 | TenantId | tenant-scoped |
| `PracticeDefaults` | 13 | TenantId | tenant-scoped |
| `Profiles` | 12 | Id | tenant-scoped |
| `SettingsChanges` | 8 | Id | tenant-scoped |
| `Tenants` | 8 | Id |  |
| `TenantIndex` | 1 | TenantId | tenant-scoped |
| `TermsAcceptances` | 5 | Id | append-only ledger; tenant-scoped |

<details><summary><code>tenancy.AccessRequests</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `DecidedAt` | timestamptz | yes |  |
| `DecidedBy` | guid | yes | id of `identity.Users` |
| `ExpiresAt` | timestamptz | yes |  |
| `FacilityId` | guid |  | FK to `Facility` |
| `PatientId` | guid |  | id of `patients.Patients` |
| `Reason` | character varying(30) |  |  |
| `RequestedAt` | timestamptz |  |  |
| `RequestedBy` | guid |  | id of `identity.Users` |
| `Status` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>tenancy.AccessReviews</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `CompletedAt` | timestamptz | yes |  |
| `Quarter` | character varying(8) |  |  |
| `StartedAt` | timestamptz |  |  |
| `TenantId` | guid |  | FK to `Tenant` |

</details>

<details><summary><code>tenancy.AccessReviewDecisions</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `ReviewId` | guid |  |  |
| `MembershipId` | guid |  |  |
| `DecidedAt` | timestamptz |  |  |
| `DecidedBy` | guid | yes | id of `identity.Users` |
| `Decision` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>tenancy.Closures</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `Date` | date |  |  |
| `FacilityId` | guid |  | FK to `Facility` |
| `Name` | character varying(100) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>tenancy.CustomRoles</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `BaseRole` | character varying(40) |  |  |
| `Capabilities` | character varying(400) |  |  |
| `CreatedAt` | timestamptz |  |  |
| `Name` | character varying(60) |  |  |
| `TenantId` | guid |  | FK to `Tenant` |

</details>

<details><summary><code>tenancy.DoctorLicences</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `MembershipId` | guid |  | FK to `Membership` |
| `AttestedAt` | timestamptz |  |  |
| `AttestedBy` | guid | yes |  |
| `Authority` | character varying(20) |  |  |
| `Category` | character varying(40) |  |  |
| `CertificateName` | character varying(200) |  |  |
| `ExpiresOn` | date | yes |  |
| `Number` | character varying(20) |  |  |
| `SubmittedAt` | timestamptz |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>tenancy.DoctorProfiles</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `MembershipId` | guid |  | FK to `Membership` |
| `ChangedAt` | timestamptz |  |  |
| `ChangedBy` | guid | yes | id of `identity.Users` |
| `FollowUpFeeMinor` | bigint |  |  |
| `FullName` | character varying(200) |  |  |
| `Languages` | character varying(200) |  |  |
| `NewVisitFeeMinor` | bigint |  |  |
| `Qualifications` | character varying(200) |  |  |
| `SignatureKey` | character varying(200) |  |  |
| `Specialty` | character varying(100) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>tenancy.Facilities</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `Address` | character varying(500) |  |  |
| `CertificateKey` | character varying(200) |  |  |
| `CertificateName` | character varying(200) |  |  |
| `Name` | character varying(200) |  |  |
| `TaxNumber` | character varying(20) |  |  |
| `TenantId` | guid |  | FK to `Tenant` |
| `TimeZone` | character varying(64) |  |  |

</details>

<details><summary><code>tenancy.FeatureFlags</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `ChangedAt` | timestamptz |  |  |
| `ChangedBy` | guid | yes | id of `identity.Users` |
| `CreatedAt` | timestamptz |  |  |
| `CreatedBy` | guid | yes | id of `identity.Users` |
| `Description` | character varying(500) |  |  |
| `Enabled` | bool |  |  |
| `Key` | character varying(100) |  |  |
| `Owner` | character varying(100) |  |  |
| `TenantId` | guid |  | FK to `Tenant` |

</details>

<details><summary><code>tenancy.Invitations</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `AcceptedAt` | timestamptz | yes |  |
| `CreatedAt` | timestamptz |  |  |
| `Email` | character varying(254) |  |  |
| `ExpiresAt` | timestamptz |  |  |
| `InvitedBy` | guid |  |  |
| `MembershipId` | guid |  | FK to `Membership` |
| `RevokedAt` | timestamptz | yes |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `TokenHash` | bytes |  |  |

</details>

<details><summary><code>tenancy.Memberships</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `CustomRoleId` | guid | yes | FK to `CustomRole` |
| `FacilityId` | guid |  | FK to `Facility` |
| `Role` | character varying(40) |  |  |
| `Status` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `UserId` | guid | yes | id of `identity.Users` |

</details>

<details><summary><code>tenancy.OperatingHours</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `Closes` | time |  |  |
| `FacilityId` | guid |  | FK to `Facility` |
| `Opens` | time |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `Weekday` | int |  |  |

</details>

<details><summary><code>tenancy.OrganisationDocuments</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `BlobKey` | character varying(120) |  |  |
| `ContentType` | character varying(40) |  |  |
| `Kind` | character varying(40) |  |  |
| `SizeBytes` | bigint |  |  |
| `TenantId` | guid |  | FK to `Tenant` |
| `UploadedAt` | timestamptz |  |  |
| `UploadedBy` | guid |  |  |

</details>

<details><summary><code>tenancy.OrganisationRequests</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `TenantId` | guid |  | FK to `Tenant` |
| `ItContactEmail` | character varying(254) |  |  |
| `ItContactName` | character varying(200) |  |  |
| `LastReason` | character varying(500) |  |  |
| `SubmittedAt` | timestamptz | yes |  |
| `Type` | character varying(20) |  |  |

</details>

<details><summary><code>tenancy.PracticeDefaults</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `TenantId` | guid |  | FK to `Tenant` |
| `Currency` | character(3) |  |  |
| `FollowUpFeeMinor` | bigint |  |  |
| `Languages` | character varying(200) |  |  |
| `NewVisitFeeMinor` | bigint |  |  |
| `NoteTemplates` | text |  |  |
| `PatientStart` | character varying(10) |  |  |
| `Qualifications` | character varying(200) |  |  |
| `SaturdayHours` | text |  |  |
| `SlotMinutes` | int |  |  |
| `Specialty` | character varying(100) |  |  |
| `WeekdayHours` | text |  |  |
| `xmin` | xid |  |  |

</details>

<details><summary><code>tenancy.Profiles</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `Address` | character varying(500) |  |  |
| `ChangedAt` | timestamptz |  |  |
| `ChangedBy` | guid | yes | id of `identity.Users` |
| `Email` | character varying(254) |  |  |
| `LogoKey` | character varying(200) |  |  |
| `Phone` | character varying(30) |  |  |
| `RegistrationExpiresOn` | date | yes |  |
| `RegistrationNumber` | character varying(50) |  |  |
| `TaxNumber` | character varying(30) |  |  |
| `TenantId` | guid |  | FK to `Tenant` |
| `xmin` | xid |  |  |

</details>

<details><summary><code>tenancy.SettingsChanges</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `ActorUserId` | guid | yes | id of `identity.Users` |
| `After` | text |  |  |
| `Before` | text |  |  |
| `OccurredAt` | timestamptz |  |  |
| `ResourceId` | guid | yes |  |
| `Section` | character varying(40) |  |  |
| `TenantId` | guid |  | FK to `Tenant` |

</details>

<details><summary><code>tenancy.Tenants</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `ClinicalAccess` | character varying(30) |  |  |
| `CountryCode` | character(2) |  |  |
| `CreatedAt` | timestamptz |  |  |
| `IdleTimeoutMinutes` | int |  |  |
| `Name` | character varying(200) |  |  |
| `ResidencyRegion` | character varying(40) |  |  |
| `State` | character varying(20) |  |  |

</details>

<details><summary><code>tenancy.TenantIndex</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>tenancy.TermsAcceptances</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `AcceptedAt` | timestamptz |  |  |
| `AcceptedBy` | guid |  |  |
| `TenantId` | guid |  | FK to `Tenant` |
| `TermsVersion` | character varying(32) |  |  |

</details>

### Identity (schema `identity`, 3 tables)

```mermaid
erDiagram
  OnboardingDrafts {
    guid UserId PK, FK "FK to Users"
  }
  Sessions {
    guid Id PK
    guid MembershipId "id of tenancy.Memberships"
    guid UserId FK "FK to Users"
  }
  Users {
    guid Id PK
  }
  tenancy_Memberships {
    guid Id PK "other module"
  }
  Users ||--o{ OnboardingDrafts : "UserId"
  Users ||--o{ Sessions : "UserId"
  tenancy_Memberships |o..o{ Sessions : "MembershipId"
```

| Table | Columns | Primary key | Notes |
|---|---|---|---|
| `OnboardingDrafts` | 6 | UserId |  |
| `Sessions` | 11 | Id | tenant-scoped |
| `Users` | 4 | Id |  |

<details><summary><code>identity.OnboardingDrafts</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `UserId` | guid |  | FK to `StaffUser` |
| `CompletedAt` | timestamptz | yes |  |
| `Country` | character(2) |  |  |
| `Data` | text |  |  |
| `Step` | int |  |  |
| `UpdatedAt` | timestamptz |  |  |

</details>

<details><summary><code>identity.Sessions</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `CreatedAt` | timestamptz |  |  |
| `Device` | character varying(64) |  |  |
| `IdleTimeoutMinutes` | int |  |  |
| `LastSeenAt` | timestamptz |  |  |
| `MembershipId` | guid | yes | id of `tenancy.Memberships` |
| `PreviousRefreshTokenHash` | bytes |  |  |
| `RefreshTokenHash` | bytes |  |  |
| `RevokedAt` | timestamptz | yes |  |
| `TenantId` | guid | yes | `tenancy.Tenants` |
| `UserId` | guid |  | FK to `StaffUser` |

</details>

<details><summary><code>identity.Users</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `DisplayName` | character varying(200) |  |  |
| `Issuer` | character varying(256) |  |  |
| `Subject` | character varying(256) |  |  |

</details>

### Patients (schema `patients`, 7 tables)

```mermaid
erDiagram
  ConsentReceipts {
    guid Id PK
    guid DecidedBy "id of identity.Users"
    guid PatientId FK "FK to Patients"
  }
  MergeRequests {
    guid Id PK
    guid CandidatePatientId FK "FK to Patients"
    guid DecidedBy "id of identity.Users"
    guid PatientId FK "FK to Patients"
  }
  Patients {
    guid Id PK
    guid ChangedBy "id of identity.Users"
    guid FacilityId "id of tenancy.Facilities"
    guid MergedIntoPatientId "id of patients.Patients"
    guid RegisteredBy "id of identity.Users"
  }
  Identifiers {
    guid Id PK
    guid MovedFromPatientId "id of patients.Patients"
    guid PatientId FK "FK to Patients"
  }
  Relationships {
    guid Id PK
    guid PatientId FK "FK to Patients"
  }
  PayerCoverages {
    guid Id PK
    guid PatientId FK "FK to Patients"
  }
  DataKeys {
    guid TenantId PK "id of tenancy.Tenants"
    charactervarying Purpose PK
  }
  identity_Users {
    guid Id PK "other module"
  }
  tenancy_Facilities {
    guid Id PK "other module"
  }
  Patients ||--o{ ConsentReceipts : "PatientId"
  identity_Users |o..o{ ConsentReceipts : "DecidedBy"
  Patients ||--o{ MergeRequests : "CandidatePatientId"
  Patients ||--o{ MergeRequests : "PatientId"
  identity_Users |o..o{ MergeRequests : "DecidedBy"
  identity_Users |o..o{ Patients : "ChangedBy"
  tenancy_Facilities ||..o{ Patients : "FacilityId"
  Patients |o..o{ Patients : "MergedIntoPatientId, no FK"
  identity_Users |o..o{ Patients : "RegisteredBy"
  Patients ||--o{ Identifiers : "PatientId"
  Patients |o..o{ Identifiers : "MovedFromPatientId, no FK"
  Patients ||--o{ Relationships : "PatientId"
  Patients ||--o{ PayerCoverages : "PatientId"
```

| Table | Columns | Primary key | Notes |
|---|---|---|---|
| `ConsentReceipts` | 10 | Id | append-only ledger; tenant-scoped |
| `MergeRequests` | 12 | Id | tenant-scoped |
| `Patients` | 26 | Id | tenant-scoped |
| `Identifiers` | 9 | Id | tenant-scoped |
| `Relationships` | 8 | Id | tenant-scoped |
| `PayerCoverages` | 9 | Id | tenant-scoped |
| `DataKeys` | 4 | TenantId, Purpose | tenant-scoped |

<details><summary><code>patients.ConsentReceipts</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `Channel` | character varying(12) |  |  |
| `DecidedAt` | timestamptz |  |  |
| `DecidedBy` | guid | yes | id of `identity.Users` |
| `Granted` | bool |  |  |
| `Jurisdiction` | character varying(8) |  |  |
| `PatientId` | guid |  | FK to `Patient` |
| `PolicyVersion` | character varying(32) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `Type` | character varying(24) |  |  |

</details>

<details><summary><code>patients.MergeRequests</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `CandidatePatientId` | guid |  | FK to `Patient` |
| `DecidedAt` | timestamptz |  |  |
| `DecidedBy` | guid | yes | id of `identity.Users` |
| `FirstApprovedBy` | guid | yes |  |
| `MergedAt` | timestamptz | yes |  |
| `PatientId` | guid |  | FK to `Patient` |
| `SecondApprovedBy` | guid | yes |  |
| `Status` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `UnmergedAt` | timestamptz | yes |  |
| `UnmergedBy` | guid | yes |  |

</details>

<details><summary><code>patients.Patients</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `AlternatePhone` | character varying(20) |  |  |
| `ArabicName` | character varying(200) |  |  |
| `ChangedAt` | timestamptz |  |  |
| `ChangedBy` | guid | yes | id of `identity.Users` |
| `CountryCode` | character(2) |  |  |
| `DateOfBirth` | date |  |  |
| `DateOfBirthEstimated` | bool |  |  |
| `Email` | character varying(254) |  |  |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `FullName` | character varying(200) |  |  |
| `GenderIdentity` | character varying(50) |  |  |
| `IdempotencyKeyHash` | character(64) |  |  |
| `IdempotencyRequestHash` | character(64) |  |  |
| `MergedIntoPatientId` | guid | yes | id of `patients.Patients` |
| `Mobile` | character varying(20) |  |  |
| `Mrn` | character varying(32) |  |  |
| `Nationality` | character(2) |  |  |
| `PreferredLanguage` | character varying(35) |  |  |
| `RegisteredAt` | timestamptz |  |  |
| `RegisteredBy` | guid | yes | id of `identity.Users` |
| `SearchKey` | text |  |  |
| `SexAtBirth` | character varying(20) |  |  |
| `Status` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `xmin` | xid |  |  |

</details>

<details><summary><code>patients.Identifiers</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `DisplayCipher` | bytes |  |  |
| `ExpiresOn` | date | yes |  |
| `LookupCipher` | bytes |  |  |
| `LookupIndex` | bytes |  |  |
| `MovedFromPatientId` | guid | yes | id of `patients.Patients` |
| `PatientId` | guid |  | FK to `Patient` |
| `Scheme` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>patients.Relationships</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `IsEmergencyContact` | bool |  |  |
| `IsGuardian` | bool |  |  |
| `Name` | character varying(200) |  |  |
| `PatientId` | guid |  | FK to `Patient` |
| `Phone` | character varying(20) |  |  |
| `Relationship` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>patients.PayerCoverages</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `Kind` | character varying(20) |  |  |
| `MemberNumberCipher` | bytes |  |  |
| `Network` | character varying(100) |  |  |
| `PatientId` | guid |  | FK to `Patient` |
| `PayerName` | character varying(200) |  |  |
| `Plan` | character varying(100) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `ValidTo` | date | yes |  |

</details>

<details><summary><code>patients.DataKeys</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `TenantId` | guid |  | `tenancy.Tenants` |
| `Purpose` | character varying(20) |  |  |
| `WrapKeyId` | character varying(300) |  |  |
| `Wrapped` | bytes |  |  |

</details>

### Scheduling (schema `scheduling`, 15 tables)

```mermaid
erDiagram
  Appointments {
    guid Id PK
    guid BookedBy "id of identity.Users"
    guid CalendarId "doctor or room"
    guid ChangedBy "id of identity.Users"
    guid FacilityId "id of tenancy.Facilities"
    guid MergedFromPatientId "id of patients.Patients"
    guid PatientId "id of patients.Patients"
  }
  AppointmentReschedules {
    guid Id PK
    guid ActorUserId "id of identity.Users"
    guid AppointmentId FK "FK to Appointments"
    guid FromCalendarId "doctor or room"
    guid ToCalendarId "doctor or room"
  }
  BookingConflicts {
    guid Id PK
    guid CalendarId "doctor or room"
    guid FacilityId "id of tenancy.Facilities"
  }
  MetricBaselines {
    guid Id PK
    guid FacilityId "id of tenancy.Facilities"
    guid RecordedBy "id of identity.Users"
  }
  OutboxEvents {
    guid Id PK
  }
  OutboxTenants {
    guid TenantId PK "id of tenancy.Tenants"
  }
  QueueDisplays {
    guid Id PK
    guid CreatedBy "id of identity.Users"
    guid FacilityId "id of tenancy.Facilities"
  }
  QueueTokens {
    guid Id PK
    guid AppointmentId FK "FK to Appointments"
    guid CalendarId "doctor or room"
    guid FacilityId "id of tenancy.Facilities"
  }
  ScheduleExceptions {
    guid Id PK
    guid CalendarId "doctor or room"
    guid CreatedBy "id of identity.Users"
    guid FacilityId "id of tenancy.Facilities"
  }
  Resources {
    guid Id PK
    guid CreatedBy "id of identity.Users"
    guid FacilityId "id of tenancy.Facilities"
  }
  ScheduleSlots {
    guid Id PK
    guid AppointmentId FK "FK to Appointments"
    guid CalendarId "doctor or room"
    guid FacilityId "id of tenancy.Facilities"
  }
  Templates {
    guid Id PK
    guid CalendarId "doctor or room"
    guid ChangedBy "id of identity.Users"
    guid FacilityId "id of tenancy.Facilities"
  }
  SlotHolds {
    guid Id PK
    guid CalendarId "doctor or room"
    guid FacilityId "id of tenancy.Facilities"
    guid HeldBy "id of identity.Users"
  }
  VisitStatusEvents {
    guid Id PK
    guid ActorUserId "id of identity.Users"
    guid AppointmentId FK "FK to Appointments"
  }
  Waitlist {
    guid Id PK
    guid AppointmentId "id of scheduling.Appointments"
    guid CalendarId "doctor or room"
    guid FacilityId "id of tenancy.Facilities"
    guid PatientId "id of patients.Patients"
    guid RequestedBy "id of identity.Users"
  }
  identity_Users {
    guid Id PK "other module"
  }
  patients_Patients {
    guid Id PK "other module"
  }
  tenancy_Facilities {
    guid Id PK "other module"
  }
  tenancy_Memberships {
    guid Id PK "other module"
  }
  identity_Users |o..o{ Appointments : "BookedBy"
  tenancy_Memberships ||..o{ Appointments : "CalendarId (doctor calendar)"
  Resources ||..o{ Appointments : "CalendarId (room calendar)"
  identity_Users |o..o{ Appointments : "ChangedBy"
  tenancy_Facilities ||..o{ Appointments : "FacilityId"
  patients_Patients |o..o{ Appointments : "MergedFromPatientId"
  patients_Patients ||..o{ Appointments : "PatientId"
  Appointments ||--o{ AppointmentReschedules : "AppointmentId"
  identity_Users |o..o{ AppointmentReschedules : "ActorUserId"
  tenancy_Memberships ||..o{ AppointmentReschedules : "FromCalendarId (doctor calendar)"
  Resources ||..o{ AppointmentReschedules : "FromCalendarId (room calendar)"
  tenancy_Memberships ||..o{ AppointmentReschedules : "ToCalendarId (doctor calendar)"
  Resources ||..o{ AppointmentReschedules : "ToCalendarId (room calendar)"
  tenancy_Memberships ||..o{ BookingConflicts : "CalendarId (doctor calendar)"
  Resources ||..o{ BookingConflicts : "CalendarId (room calendar)"
  tenancy_Facilities ||..o{ BookingConflicts : "FacilityId"
  tenancy_Facilities ||..o{ MetricBaselines : "FacilityId"
  identity_Users |o..o{ MetricBaselines : "RecordedBy"
  identity_Users |o..o{ QueueDisplays : "CreatedBy"
  tenancy_Facilities ||..o{ QueueDisplays : "FacilityId"
  Appointments ||--o{ QueueTokens : "AppointmentId"
  tenancy_Memberships ||..o{ QueueTokens : "CalendarId (doctor calendar)"
  Resources ||..o{ QueueTokens : "CalendarId (room calendar)"
  tenancy_Facilities ||..o{ QueueTokens : "FacilityId"
  tenancy_Memberships ||..o{ ScheduleExceptions : "CalendarId (doctor calendar)"
  Resources ||..o{ ScheduleExceptions : "CalendarId (room calendar)"
  identity_Users |o..o{ ScheduleExceptions : "CreatedBy"
  tenancy_Facilities ||..o{ ScheduleExceptions : "FacilityId"
  identity_Users |o..o{ Resources : "CreatedBy"
  tenancy_Facilities ||..o{ Resources : "FacilityId"
  Appointments ||--o{ ScheduleSlots : "AppointmentId"
  tenancy_Memberships ||..o{ ScheduleSlots : "CalendarId (doctor calendar)"
  Resources ||..o{ ScheduleSlots : "CalendarId (room calendar)"
  tenancy_Facilities ||..o{ ScheduleSlots : "FacilityId"
  tenancy_Memberships ||..o{ Templates : "CalendarId (doctor calendar)"
  Resources ||..o{ Templates : "CalendarId (room calendar)"
  identity_Users |o..o{ Templates : "ChangedBy"
  tenancy_Facilities ||..o{ Templates : "FacilityId"
  tenancy_Memberships ||..o{ SlotHolds : "CalendarId (doctor calendar)"
  Resources ||..o{ SlotHolds : "CalendarId (room calendar)"
  tenancy_Facilities ||..o{ SlotHolds : "FacilityId"
  identity_Users ||..o{ SlotHolds : "HeldBy"
  Appointments ||--o{ VisitStatusEvents : "AppointmentId"
  identity_Users |o..o{ VisitStatusEvents : "ActorUserId"
  Appointments |o..o{ Waitlist : "AppointmentId, no FK"
  tenancy_Memberships |o..o{ Waitlist : "CalendarId (doctor calendar)"
  Resources |o..o{ Waitlist : "CalendarId (room calendar)"
  tenancy_Facilities ||..o{ Waitlist : "FacilityId"
  patients_Patients ||..o{ Waitlist : "PatientId"
  identity_Users |o..o{ Waitlist : "RequestedBy"
```

| Table | Columns | Primary key | Notes |
|---|---|---|---|
| `Appointments` | 24 | Id | tenant-scoped |
| `AppointmentReschedules` | 12 | Id | tenant-scoped |
| `BookingConflicts` | 5 | Id | tenant-scoped |
| `MetricBaselines` | 10 | Id | tenant-scoped |
| `OutboxEvents` | 7 | Id | tenant-scoped |
| `OutboxTenants` | 1 | TenantId | tenant-scoped |
| `QueueDisplays` | 9 | Id | tenant-scoped |
| `QueueTokens` | 9 | Id | tenant-scoped |
| `ScheduleExceptions` | 12 | Id | tenant-scoped |
| `Resources` | 6 | Id | tenant-scoped |
| `ScheduleSlots` | 6 | Id | tenant-scoped |
| `Templates` | 8 | Id | tenant-scoped |
| `SlotHolds` | 9 | Id | tenant-scoped |
| `VisitStatusEvents` | 9 | Id | tenant-scoped |
| `Waitlist` | 18 | Id | tenant-scoped |

<details><summary><code>scheduling.Appointments</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `BookedAt` | timestamptz |  |  |
| `BookedBy` | guid | yes | id of `identity.Users` |
| `CalendarId` | guid |  | doctor or room (`CalendarKind`) |
| `CalendarKind` | character varying(20) |  |  |
| `ChangedAt` | timestamptz |  |  |
| `ChangedBy` | guid | yes | id of `identity.Users` |
| `Channel` | character varying(20) |  |  |
| `End` | timestamptz |  |  |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `IdempotencyKeyHash` | character(64) |  |  |
| `IdempotencyRequestHash` | character(64) |  |  |
| `MergedFromPatientId` | guid | yes | id of `patients.Patients` |
| `PatientId` | guid |  | id of `patients.Patients` |
| `ReasonCategory` | character varying(30) |  |  |
| `ReasonText` | character varying(500) |  |  |
| `Room` | character varying(50) |  |  |
| `SeriesId` | guid | yes |  |
| `Start` | timestamptz |  |  |
| `State` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `Unscheduled` | bool |  |  |
| `VisitType` | character varying(30) |  |  |
| `WalkIn` | bool |  |  |

</details>

<details><summary><code>scheduling.AppointmentReschedules</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `ActorUserId` | guid | yes | id of `identity.Users` |
| `AppointmentId` | guid |  | FK to `Appointment` |
| `FromCalendarId` | guid |  | doctor or room (`CalendarKind`) |
| `FromEnd` | timestamptz |  |  |
| `FromStart` | timestamptz |  |  |
| `OccurredAt` | timestamptz |  |  |
| `ReassignReason` | character varying(30) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `ToCalendarId` | guid |  | doctor or room (`CalendarKind`) |
| `ToEnd` | timestamptz |  |  |
| `ToStart` | timestamptz |  |  |

</details>

<details><summary><code>scheduling.BookingConflicts</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `CalendarId` | guid |  | doctor or room (`CalendarKind`) |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `OccurredAt` | timestamptz |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>scheduling.MetricBaselines</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `FromDate` | date |  |  |
| `Method` | character varying(200) |  |  |
| `Metric` | character varying(30) |  |  |
| `RecordedAt` | timestamptz |  |  |
| `RecordedBy` | guid | yes | id of `identity.Users` |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `ToDate` | date |  |  |
| `Value` | int |  |  |

</details>

<details><summary><code>scheduling.OutboxEvents</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `AggregateId` | guid |  |  |
| `OccurredAt` | timestamptz |  |  |
| `Payload` | text |  |  |
| `PublishedAt` | timestamptz | yes |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `Type` | character varying(100) |  |  |

</details>

<details><summary><code>scheduling.OutboxTenants</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>scheduling.QueueDisplays</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `CreatedAt` | timestamptz |  |  |
| `CreatedBy` | guid | yes | id of `identity.Users` |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `Name` | character varying(60) |  |  |
| `RevokedAt` | timestamptz | yes |  |
| `RevokedBy` | guid | yes |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `TokenHash` | bytes |  |  |

</details>

<details><summary><code>scheduling.QueueTokens</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `AppointmentId` | guid |  | FK to `Appointment` |
| `CalendarId` | guid |  | doctor or room (`CalendarKind`) |
| `Date` | date |  |  |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `IssuedAt` | timestamptz |  |  |
| `Number` | int |  |  |
| `Prefix` | character varying(4) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>scheduling.ScheduleExceptions</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `CalendarId` | guid |  | doctor or room (`CalendarKind`) |
| `CreatedAt` | timestamptz |  |  |
| `CreatedBy` | guid | yes | id of `identity.Users` |
| `End` | time | yes |  |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `FromDate` | date |  |  |
| `Kind` | character varying(20) |  |  |
| `Reason` | character varying(60) |  |  |
| `Start` | time | yes |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `ToDate` | date |  |  |

</details>

<details><summary><code>scheduling.Resources</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `CreatedAt` | timestamptz |  |  |
| `CreatedBy` | guid | yes | id of `identity.Users` |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `Name` | character varying(100) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>scheduling.ScheduleSlots</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `AppointmentId` | guid |  | FK to `Appointment` |
| `CalendarId` | guid |  | doctor or room (`CalendarKind`) |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `SlotStart` | timestamptz |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>scheduling.Templates</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `CalendarId` | guid |  | doctor or room (`CalendarKind`) |
| `CalendarKind` | character varying(20) |  |  |
| `ChangedAt` | timestamptz |  |  |
| `ChangedBy` | guid | yes | id of `identity.Users` |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `SlotMinutes` | int |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>scheduling.SlotHolds</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `CalendarId` | guid |  | doctor or room (`CalendarKind`) |
| `End` | timestamptz |  |  |
| `ExpiresAt` | timestamptz |  |  |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `HeldBy` | guid |  | id of `identity.Users` |
| `Start` | timestamptz |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `VisitType` | character varying(30) |  |  |

</details>

<details><summary><code>scheduling.VisitStatusEvents</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `ActorUserId` | guid | yes | id of `identity.Users` |
| `AppointmentId` | guid |  | FK to `Appointment` |
| `CancelReason` | character varying(30) |  |  |
| `CheckInMs` | int | yes |  |
| `Note` | character varying(500) |  |  |
| `OccurredAt` | timestamptz |  |  |
| `State` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>scheduling.Waitlist</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `AppointmentId` | guid | yes | id of `scheduling.Appointments` |
| `CalendarId` | guid | yes | doctor or room (`CalendarKind`) |
| `ChangedAt` | timestamptz |  |  |
| `Days` | int |  |  |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `From` | time | yes |  |
| `OfferCalendarId` | guid | yes |  |
| `OfferExpiresAt` | timestamptz | yes |  |
| `OfferStart` | timestamptz | yes |  |
| `PatientId` | guid |  | id of `patients.Patients` |
| `ReasonCategory` | character varying(30) |  |  |
| `RequestedAt` | timestamptz |  |  |
| `RequestedBy` | guid | yes | id of `identity.Users` |
| `State` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `Until` | time | yes |  |
| `VisitType` | character varying(30) |  |  |

</details>

### Encounters (schema `encounters`, 13 tables)

```mermaid
erDiagram
  Allergies {
    guid Id PK
    guid FacilityId "id of tenancy.Facilities"
    guid MergedFromPatientId "id of patients.Patients"
    guid PatientId "id of patients.Patients"
    guid RecordedBy "id of identity.Users"
    guid StatusSetBy "id of identity.Users"
  }
  ClinicalDocuments {
    guid Id PK
    guid ChangedBy "id of identity.Users"
    guid FacilityId "id of tenancy.Facilities"
    guid PatientId "id of patients.Patients"
  }
  ConsultNotes {
    guid Id PK
    guid EncounterId "id of encounters.Encounters"
  }
  DoctorNoteTemplates {
    guid Id PK
    guid UserId "id of identity.Users"
  }
  Encounters {
    guid Id PK
    guid AppointmentId "id of scheduling.Appointments"
    guid EndedBy "id of identity.Users"
    guid FacilityId "id of tenancy.Facilities"
    guid LeadClinicianId "id of identity.Users"
    guid MergedFromPatientId "id of patients.Patients"
    guid PatientId "id of patients.Patients"
  }
  Intakes {
    guid Id PK
    guid AppointmentId "id of scheduling.Appointments"
    guid FacilityId "id of tenancy.Facilities"
    guid HandedOffBy "id of identity.Users"
    guid MergedFromPatientId "id of patients.Patients"
    guid NoKnownAllergiesBy "id of identity.Users"
    guid PatientId "id of patients.Patients"
  }
  IntakeEscalations {
    guid Id PK
    guid AppointmentId "id of scheduling.Appointments"
    guid EscalatedBy "id of identity.Users"
    guid FacilityId "id of tenancy.Facilities"
    guid MergedFromPatientId "id of patients.Patients"
    guid PatientId "id of patients.Patients"
  }
  MedicationDecisions {
    guid Id PK
    guid DecidedBy "id of identity.Users"
    guid EncounterId "id of encounters.Encounters"
    guid MergedFromPatientId "id of patients.Patients"
    guid PatientId "id of patients.Patients"
  }
  MedicationReconciliations {
    guid Id PK
    guid EncounterId "id of encounters.Encounters"
    guid MergedFromPatientId "id of patients.Patients"
    guid PatientId "id of patients.Patients"
  }
  MedicationStatements {
    guid Id PK
    guid AppointmentId "id of scheduling.Appointments"
    guid FacilityId "id of tenancy.Facilities"
    guid MergedFromPatientId "id of patients.Patients"
    guid PatientId "id of patients.Patients"
    guid RecordedBy "id of identity.Users"
  }
  Observations {
    guid Id PK
    guid AppointmentId "id of scheduling.Appointments"
    guid FacilityId "id of tenancy.Facilities"
    guid MergedFromPatientId "id of patients.Patients"
    guid PatientId "id of patients.Patients"
    guid RecordedBy "id of identity.Users"
    guid VerifiedBy "id of identity.Users"
  }
  Problems {
    guid Id PK
    guid PatientId "id of patients.Patients"
  }
  SignedNoteVersions {
    guid Id PK
    guid EncounterId "id of encounters.Encounters"
  }
  identity_Users {
    guid Id PK "other module"
  }
  patients_Patients {
    guid Id PK "other module"
  }
  scheduling_Appointments {
    guid Id PK "other module"
  }
  tenancy_Facilities {
    guid Id PK "other module"
  }
  tenancy_Facilities |o..o{ Allergies : "FacilityId"
  patients_Patients |o..o{ Allergies : "MergedFromPatientId"
  patients_Patients ||..o{ Allergies : "PatientId"
  identity_Users |o..o{ Allergies : "RecordedBy"
  identity_Users |o..o{ Allergies : "StatusSetBy"
  identity_Users |o..o{ ClinicalDocuments : "ChangedBy"
  tenancy_Facilities ||..o{ ClinicalDocuments : "FacilityId"
  patients_Patients ||..o{ ClinicalDocuments : "PatientId"
  Encounters ||..o{ ConsultNotes : "EncounterId, no FK"
  identity_Users ||..o{ DoctorNoteTemplates : "UserId"
  scheduling_Appointments ||..o{ Encounters : "AppointmentId"
  identity_Users |o..o{ Encounters : "EndedBy"
  tenancy_Facilities ||..o{ Encounters : "FacilityId"
  identity_Users ||..o{ Encounters : "LeadClinicianId"
  patients_Patients |o..o{ Encounters : "MergedFromPatientId"
  patients_Patients ||..o{ Encounters : "PatientId"
  scheduling_Appointments ||..o{ Intakes : "AppointmentId"
  tenancy_Facilities ||..o{ Intakes : "FacilityId"
  identity_Users |o..o{ Intakes : "HandedOffBy"
  patients_Patients |o..o{ Intakes : "MergedFromPatientId"
  identity_Users |o..o{ Intakes : "NoKnownAllergiesBy"
  patients_Patients ||..o{ Intakes : "PatientId"
  scheduling_Appointments ||..o{ IntakeEscalations : "AppointmentId"
  identity_Users ||..o{ IntakeEscalations : "EscalatedBy"
  tenancy_Facilities ||..o{ IntakeEscalations : "FacilityId"
  patients_Patients |o..o{ IntakeEscalations : "MergedFromPatientId"
  patients_Patients ||..o{ IntakeEscalations : "PatientId"
  identity_Users ||..o{ MedicationDecisions : "DecidedBy"
  Encounters ||..o{ MedicationDecisions : "EncounterId, no FK"
  patients_Patients |o..o{ MedicationDecisions : "MergedFromPatientId"
  patients_Patients ||..o{ MedicationDecisions : "PatientId"
  Encounters ||..o{ MedicationReconciliations : "EncounterId, no FK"
  patients_Patients |o..o{ MedicationReconciliations : "MergedFromPatientId"
  patients_Patients ||..o{ MedicationReconciliations : "PatientId"
  scheduling_Appointments |o..o{ MedicationStatements : "AppointmentId"
  tenancy_Facilities |o..o{ MedicationStatements : "FacilityId"
  patients_Patients |o..o{ MedicationStatements : "MergedFromPatientId"
  patients_Patients ||..o{ MedicationStatements : "PatientId"
  identity_Users |o..o{ MedicationStatements : "RecordedBy"
  scheduling_Appointments |o..o{ Observations : "AppointmentId"
  tenancy_Facilities |o..o{ Observations : "FacilityId"
  patients_Patients |o..o{ Observations : "MergedFromPatientId"
  patients_Patients ||..o{ Observations : "PatientId"
  identity_Users |o..o{ Observations : "RecordedBy"
  identity_Users |o..o{ Observations : "VerifiedBy"
  patients_Patients ||..o{ Problems : "PatientId"
  Encounters ||..o{ SignedNoteVersions : "EncounterId, no FK"
```

| Table | Columns | Primary key | Notes |
|---|---|---|---|
| `Allergies` | 14 | Id | tenant-scoped |
| `ClinicalDocuments` | 16 | Id | tenant-scoped |
| `ConsultNotes` | 11 | Id | tenant-scoped |
| `DoctorNoteTemplates` | 3 | Id | tenant-scoped |
| `Encounters` | 12 | Id | tenant-scoped |
| `Intakes` | 12 | Id | tenant-scoped |
| `IntakeEscalations` | 9 | Id | tenant-scoped |
| `MedicationDecisions` | 13 | Id | tenant-scoped |
| `MedicationReconciliations` | 7 | Id | tenant-scoped |
| `MedicationStatements` | 17 | Id | tenant-scoped |
| `Observations` | 16 | Id | tenant-scoped |
| `Problems` | 9 | Id | tenant-scoped |
| `SignedNoteVersions` | 12 | Id | append-only ledger; tenant-scoped |

<details><summary><code>encounters.Allergies</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `FacilityId` | guid | yes | id of `tenancy.Facilities` |
| `Kind` | character varying(20) |  |  |
| `MergedFromPatientId` | guid | yes | id of `patients.Patients` |
| `PatientId` | guid |  | id of `patients.Patients` |
| `Reaction` | character varying(200) |  |  |
| `RecordedAt` | timestamptz |  |  |
| `RecordedBy` | guid | yes | id of `identity.Users` |
| `Source` | character varying(20) |  |  |
| `Status` | character varying(20) |  |  |
| `StatusSetAt` | timestamptz | yes |  |
| `StatusSetBy` | guid | yes | id of `identity.Users` |
| `Substance` | character varying(200) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>encounters.ClinicalDocuments</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `BlobKey` | character varying(64) |  |  |
| `ChangedAt` | timestamptz | yes |  |
| `ChangedBy` | guid | yes | id of `identity.Users` |
| `ContentType` | character varying(50) |  |  |
| `DocumentDate` | date |  |  |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `PatientId` | guid |  | id of `patients.Patients` |
| `ScanStatus` | character varying(20) |  |  |
| `Sha256` | character varying(64) |  |  |
| `SizeBytes` | bigint |  |  |
| `Source` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `Type` | character varying(20) |  |  |
| `UploadedAt` | timestamptz |  |  |
| `UploadedBy` | guid |  |  |

</details>

<details><summary><code>encounters.ConsultNotes</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `EncounterId` | guid |  | id of `encounters.Encounters` |
| `Manual` | bool |  |  |
| `Sections` | jsonb |  |  |
| `Status` | character varying(20) |  |  |
| `TemplateKey` | character varying(50) |  |  |
| `TemplateVersion` | int |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `UpdatedAt` | timestamptz |  |  |
| `UpdatedBy` | guid |  |  |
| `Version` | int |  |  |

</details>

<details><summary><code>encounters.DoctorNoteTemplates</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `UserId` | guid |  | id of `identity.Users` |

</details>

<details><summary><code>encounters.Encounters</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `AppointmentId` | guid |  | id of `scheduling.Appointments` |
| `AuthorityMode` | character varying(20) |  |  |
| `EndedAt` | timestamptz | yes |  |
| `EndedBy` | guid | yes | id of `identity.Users` |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `LeadClinicianId` | guid |  | id of `identity.Users` |
| `MergedFromPatientId` | guid | yes | id of `patients.Patients` |
| `PatientId` | guid |  | id of `patients.Patients` |
| `StartedAt` | timestamptz |  |  |
| `Status` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>encounters.Intakes</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `AppointmentId` | guid |  | id of `scheduling.Appointments` |
| `ChiefComplaint` | character varying(500) |  |  |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `HandedOffAt` | timestamptz | yes |  |
| `HandedOffBy` | guid | yes | id of `identity.Users` |
| `MergedFromPatientId` | guid | yes | id of `patients.Patients` |
| `NoKnownAllergiesAt` | timestamptz | yes |  |
| `NoKnownAllergiesBy` | guid | yes | id of `identity.Users` |
| `PatientId` | guid |  | id of `patients.Patients` |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `UpdatedAt` | timestamptz |  |  |

</details>

<details><summary><code>encounters.IntakeEscalations</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `AppointmentId` | guid |  | id of `scheduling.Appointments` |
| `EscalatedAt` | timestamptz |  |  |
| `EscalatedBy` | guid |  | id of `identity.Users` |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `MergedFromPatientId` | guid | yes | id of `patients.Patients` |
| `PatientId` | guid |  | id of `patients.Patients` |
| `Reason` | character varying(500) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>encounters.MedicationDecisions</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `DecidedAt` | timestamptz |  |  |
| `DecidedBy` | guid |  | id of `identity.Users` |
| `EncounterId` | guid |  | id of `encounters.Encounters` |
| `FromDose` | character varying(100) |  |  |
| `Kind` | character varying(20) |  |  |
| `MedicineKey` | character varying(200) |  |  |
| `MergedFromPatientId` | guid | yes | id of `patients.Patients` |
| `Name` | character varying(200) |  |  |
| `NewDose` | character varying(100) |  |  |
| `PatientId` | guid |  | id of `patients.Patients` |
| `Reason` | character varying(500) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>encounters.MedicationReconciliations</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `ConfirmedAt` | timestamptz |  |  |
| `ConfirmedBy` | guid |  |  |
| `EncounterId` | guid |  | id of `encounters.Encounters` |
| `MergedFromPatientId` | guid | yes | id of `patients.Patients` |
| `PatientId` | guid |  | id of `patients.Patients` |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>encounters.MedicationStatements</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `AppointmentId` | guid | yes | id of `scheduling.Appointments` |
| `Dose` | character varying(100) |  |  |
| `FacilityId` | guid | yes | id of `tenancy.Facilities` |
| `Frequency` | character varying(50) |  |  |
| `MergedFromPatientId` | guid | yes | id of `patients.Patients` |
| `Name` | character varying(200) |  |  |
| `NeedsClarification` | bool |  |  |
| `PatientId` | guid |  | id of `patients.Patients` |
| `RecordedAt` | timestamptz |  |  |
| `RecordedBy` | guid | yes | id of `identity.Users` |
| `ReviewedAt` | timestamptz | yes |  |
| `ReviewedBy` | guid | yes |  |
| `Source` | character varying(20) |  |  |
| `State` | character varying(20) |  |  |
| `StoppedAt` | timestamptz | yes |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>encounters.Observations</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `AppointmentId` | guid | yes | id of `scheduling.Appointments` |
| `Code` | character varying(30) |  |  |
| `FacilityId` | guid | yes | id of `tenancy.Facilities` |
| `Flag` | character varying(20) |  |  |
| `FlagKept` | bool |  |  |
| `MergedFromPatientId` | guid | yes | id of `patients.Patients` |
| `PatientId` | guid |  | id of `patients.Patients` |
| `PreviousAt` | timestamptz | yes |  |
| `PreviousTenths` | int | yes |  |
| `RecordedBy` | guid | yes | id of `identity.Users` |
| `Source` | character varying(20) |  |  |
| `TakenAt` | timestamptz |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `ValueHundredths` | int |  |  |
| `VerifiedBy` | guid | yes | id of `identity.Users` |

</details>

<details><summary><code>encounters.Problems</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `Description` | character varying(200) |  |  |
| `Icd10Code` | character varying(10) |  |  |
| `OnsetOn` | date | yes |  |
| `PatientId` | guid |  | id of `patients.Patients` |
| `RecordedAt` | timestamptz |  |  |
| `Source` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `Verification` | character varying(20) |  |  |

</details>

<details><summary><code>encounters.SignedNoteVersions</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `ContentHash` | character varying(64) |  |  |
| `EncounterId` | guid |  | id of `encounters.Encounters` |
| `Manual` | bool |  |  |
| `NoteId` | guid |  |  |
| `Sections` | jsonb |  |  |
| `SignedAt` | timestamptz |  |  |
| `SignedBy` | guid |  |  |
| `TemplateKey` | character varying(50) |  |  |
| `TemplateVersion` | int |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `VersionNo` | int |  |  |

</details>

### Prescribing (schema `prescribing`, 5 tables)

```mermaid
erDiagram
  Orders {
    guid Id PK
    guid CreatedBy "id of identity.Users"
    guid EncounterId "id of encounters.Encounters"
    guid FacilityId "id of tenancy.Facilities"
    guid MergedFromPatientId "id of patients.Patients"
    guid OrderedBy "id of identity.Users"
    guid PatientId "id of patients.Patients"
  }
  FormularyItems {
    guid Id PK
    guid AddedBy "id of identity.Users"
  }
  OrderSets {
    guid Id PK
    guid CreatedBy "id of identity.Users"
  }
  Referrals {
    guid Id PK
    guid CreatedBy "id of identity.Users"
    guid EncounterId "id of encounters.Encounters"
    guid FacilityId "id of tenancy.Facilities"
    guid MergedFromPatientId "id of patients.Patients"
    guid PatientId "id of patients.Patients"
    guid SentBy "id of identity.Users"
  }
  ReferralEvents {
    guid Id PK
  }
  encounters_Encounters {
    guid Id PK "other module"
  }
  identity_Users {
    guid Id PK "other module"
  }
  patients_Patients {
    guid Id PK "other module"
  }
  tenancy_Facilities {
    guid Id PK "other module"
  }
  identity_Users ||..o{ Orders : "CreatedBy"
  encounters_Encounters ||..o{ Orders : "EncounterId"
  tenancy_Facilities ||..o{ Orders : "FacilityId"
  patients_Patients |o..o{ Orders : "MergedFromPatientId"
  identity_Users |o..o{ Orders : "OrderedBy"
  patients_Patients ||..o{ Orders : "PatientId"
  identity_Users |o..o{ FormularyItems : "AddedBy"
  identity_Users ||..o{ OrderSets : "CreatedBy"
  identity_Users ||..o{ Referrals : "CreatedBy"
  encounters_Encounters |o..o{ Referrals : "EncounterId"
  tenancy_Facilities ||..o{ Referrals : "FacilityId"
  patients_Patients |o..o{ Referrals : "MergedFromPatientId"
  patients_Patients ||..o{ Referrals : "PatientId"
  identity_Users |o..o{ Referrals : "SentBy"
```

| Table | Columns | Primary key | Notes |
|---|---|---|---|
| `Orders` | 15 | Id | tenant-scoped |
| `FormularyItems` | 14 | Id | tenant-scoped |
| `OrderSets` | 5 | Id | tenant-scoped |
| `Referrals` | 19 | Id | tenant-scoped |
| `ReferralEvents` | 8 | Id | tenant-scoped |

<details><summary><code>prescribing.Orders</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `CreatedAt` | timestamptz |  |  |
| `CreatedBy` | guid |  | id of `identity.Users` |
| `Destination` | character varying(100) |  |  |
| `EncounterId` | guid |  | id of `encounters.Encounters` |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `Kind` | character varying(20) |  |  |
| `MergedFromPatientId` | guid | yes | id of `patients.Patients` |
| `Name` | character varying(200) |  |  |
| `Note` | character varying(200) |  |  |
| `OrderedAt` | timestamptz | yes |  |
| `OrderedBy` | guid | yes | id of `identity.Users` |
| `PatientId` | guid |  | id of `patients.Patients` |
| `State` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>prescribing.FormularyItems</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `Active` | bool |  |  |
| `AddedAt` | timestamptz | yes |  |
| `AddedBy` | guid | yes | id of `identity.Users` |
| `Atc` | character varying(7) |  |  |
| `Country` | character varying(2) |  |  |
| `DefaultSig` | character varying(200) |  |  |
| `Form` | character varying(40) |  |  |
| `Generic` | character varying(200) |  |  |
| `NationalCode` | character varying(30) |  |  |
| `RegClass` | character varying(20) |  |  |
| `Source` | character varying(20) |  |  |
| `Strength` | character varying(50) |  |  |
| `TenantId` | guid | yes | `tenancy.Tenants` |

</details>

<details><summary><code>prescribing.OrderSets</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `CreatedAt` | timestamptz |  |  |
| `CreatedBy` | guid |  | id of `identity.Users` |
| `Name` | character varying(100) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>prescribing.Referrals</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `Channel` | character varying(20) |  |  |
| `ConsentDecidedAt` | timestamptz | yes |  |
| `CreatedAt` | timestamptz |  |  |
| `CreatedBy` | guid |  | id of `identity.Users` |
| `Direction` | character varying(10) |  |  |
| `EncounterId` | guid | yes | id of `encounters.Encounters` |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `LetterDocumentId` | guid | yes |  |
| `MergedFromPatientId` | guid | yes | id of `patients.Patients` |
| `PatientId` | guid |  | id of `patients.Patients` |
| `Provider` | character varying(200) |  |  |
| `Reason` | character varying(500) |  |  |
| `Sections` | character varying(60) |  |  |
| `SentAt` | timestamptz | yes |  |
| `SentBy` | guid | yes | id of `identity.Users` |
| `State` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `Urgency` | character varying(10) |  |  |

</details>

<details><summary><code>prescribing.ReferralEvents</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `At` | timestamptz |  |  |
| `By` | guid |  |  |
| `DocumentId` | guid | yes |  |
| `Note` | character varying(200) |  |  |
| `ReferralId` | guid |  |  |
| `State` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

### Notifications (schema `notifications`, 5 tables)

```mermaid
erDiagram
  Notifications {
    guid Id PK
    guid AppointmentId "id of scheduling.Appointments"
    guid FacilityId "id of tenancy.Facilities"
    guid PatientId "id of patients.Patients"
  }
  Attempts {
    guid Id PK
    guid NotificationId "id of notifications.Notifications"
  }
  Preferences {
    guid TenantId PK "id of tenancy.Tenants"
    guid ChangedBy "id of identity.Users"
  }
  StaffNotifications {
    guid Id PK
    guid FacilityId "id of tenancy.Facilities"
  }
  StaffNotificationReads {
    guid NotificationId PK "id of notifications.Notifications"
    guid UserId PK "id of identity.Users"
  }
  identity_Users {
    guid Id PK "other module"
  }
  patients_Patients {
    guid Id PK "other module"
  }
  scheduling_Appointments {
    guid Id PK "other module"
  }
  tenancy_Facilities {
    guid Id PK "other module"
  }
  scheduling_Appointments ||..o{ Notifications : "AppointmentId"
  tenancy_Facilities ||..o{ Notifications : "FacilityId"
  patients_Patients ||..o{ Notifications : "PatientId"
  Notifications ||..o{ Attempts : "NotificationId, no FK"
  identity_Users |o..o{ Preferences : "ChangedBy"
  tenancy_Facilities ||..o{ StaffNotifications : "FacilityId"
  Notifications ||..o{ StaffNotificationReads : "NotificationId, no FK"
  identity_Users ||..o{ StaffNotificationReads : "UserId"
```

| Table | Columns | Primary key | Notes |
|---|---|---|---|
| `Notifications` | 17 | Id | tenant-scoped |
| `Attempts` | 6 | Id | tenant-scoped |
| `Preferences` | 12 | TenantId | tenant-scoped |
| `StaffNotifications` | 8 | Id | tenant-scoped |
| `StaffNotificationReads` | 4 | NotificationId, UserId | tenant-scoped |

<details><summary><code>notifications.Notifications</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `AppointmentId` | guid |  | id of `scheduling.Appointments` |
| `AppointmentStart` | timestamptz |  |  |
| `Attempts` | int |  |  |
| `Channel` | character varying(10) |  |  |
| `CreatedAt` | timestamptz |  |  |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `FailureCode` | character varying(60) |  |  |
| `Kind` | character varying(20) |  |  |
| `NextAttemptAt` | timestamptz | yes |  |
| `PatientId` | guid |  | id of `patients.Patients` |
| `ProviderRef` | character varying(80) |  |  |
| `SendAt` | timestamptz |  |  |
| `SourceEventId` | guid |  |  |
| `Status` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |
| `UpdatedAt` | timestamptz |  |  |

</details>

<details><summary><code>notifications.Attempts</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `At` | timestamptz |  |  |
| `Code` | character varying(60) |  |  |
| `NotificationId` | guid |  | id of `notifications.Notifications` |
| `Outcome` | character varying(20) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>notifications.Preferences</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `TenantId` | guid |  | `tenancy.Tenants` |
| `ChangedAt` | timestamptz |  |  |
| `ChangedBy` | guid | yes | id of `identity.Users` |
| `Changes` | bool |  |  |
| `Confirmation` | bool |  |  |
| `Email` | bool |  |  |
| `QuietEnd` | time |  |  |
| `QuietStart` | time |  |  |
| `Reminder24h` | bool |  |  |
| `Reminder2h` | bool |  |  |
| `SenderName` | character varying(60) |  |  |
| `Sms` | bool |  |  |

</details>

<details><summary><code>notifications.StaffNotifications</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `CreatedAt` | timestamptz |  |  |
| `DedupeKey` | character varying(80) |  |  |
| `Detail` | int | yes |  |
| `FacilityId` | guid |  | id of `tenancy.Facilities` |
| `Kind` | character varying(30) |  |  |
| `RefId` | guid | yes |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

<details><summary><code>notifications.StaffNotificationReads</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `NotificationId` | guid |  |  |
| `UserId` | guid |  |  |
| `ReadAt` | timestamptz |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

### Audit (schema `audit`, 1 table)

```mermaid
erDiagram
  AuditEvents {
    guid Id PK
    guid ActorUserId "id of identity.Users"
    guid FacilityId "id of tenancy.Facilities"
    guid PatientId "id of patients.Patients"
  }
  identity_Users {
    guid Id PK "other module"
  }
  patients_Patients {
    guid Id PK "other module"
  }
  tenancy_Facilities {
    guid Id PK "other module"
  }
  identity_Users |o..o{ AuditEvents : "ActorUserId"
  tenancy_Facilities |o..o{ AuditEvents : "FacilityId"
  patients_Patients |o..o{ AuditEvents : "PatientId"
```

| Table | Columns | Primary key | Notes |
|---|---|---|---|
| `AuditEvents` | 15 | Id | append-only ledger; tenant-scoped |

<details><summary><code>audit.AuditEvents</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `Action` | character varying(64) |  |  |
| `ActorUserId` | guid | yes | id of `identity.Users` |
| `ClientIp` | character varying(45) |  |  |
| `CorrelationId` | character varying(64) |  |  |
| `Detail` | character varying(256) |  |  |
| `FacilityId` | guid | yes | id of `tenancy.Facilities` |
| `OccurredAt` | timestamptz |  |  |
| `Outcome` | character varying(16) |  |  |
| `PatientId` | guid | yes | id of `patients.Patients` |
| `Purpose` | character varying(32) |  |  |
| `ResourceId` | guid | yes |  |
| `ResourceType` | character varying(64) |  |  |
| `Role` | character varying(32) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

### Leads (schema `leads`, 2 tables)

```mermaid
erDiagram
  DemoRequests {
    guid Id PK
  }
  Waitlist {
    guid Id PK
  }
```

| Table | Columns | Primary key | Notes |
|---|---|---|---|
| `DemoRequests` | 12 | Id |  |
| `Waitlist` | 8 | Id |  |

<details><summary><code>leads.DemoRequests</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `ConsentedAt` | timestamptz | yes |  |
| `Country` | character varying(5) |  |  |
| `Email` | character varying(254) |  |  |
| `Message` | character varying(1000) |  |  |
| `Name` | character varying(100) |  |  |
| `NoticeVersion` | character varying(20) |  |  |
| `Organisation` | character varying(150) |  |  |
| `Phone` | character varying(20) |  |  |
| `PreferredAt` | timestamptz | yes |  |
| `ReceivedAt` | timestamptz |  |  |
| `Role` | character varying(100) |  |  |

</details>

<details><summary><code>leads.Waitlist</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `ConsentedAt` | timestamptz | yes |  |
| `Country` | character varying(2) |  |  |
| `Email` | character varying(254) |  |  |
| `Name` | character varying(100) |  |  |
| `NoticeVersion` | character varying(20) |  |  |
| `Phone` | character varying(20) |  |  |
| `ReceivedAt` | timestamptz |  |  |

</details>

### Billing (schema `billing`, 1 table)

```mermaid
erDiagram
  DocSequences {
    guid Id PK
  }
```

| Table | Columns | Primary key | Notes |
|---|---|---|---|
| `DocSequences` | 7 | Id | tenant-scoped |

<details><summary><code>billing.DocSequences</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `CountryCode` | character(2) |  |  |
| `DocumentType` | character varying(20) |  |  |
| `FinancialYear` | character varying(5) |  |  |
| `NextNumber` | bigint |  |  |
| `Prefix` | character varying(16) |  |  |
| `TenantId` | guid |  | `tenancy.Tenants` |

</details>

### Platform (schema `platform`, 5 tables)

```mermaid
erDiagram
  KeyLinks {
    guid Id PK
    guid MemberId FK "FK to Members"
  }
  AuditEntries {
    guid Id PK
  }
  Members {
    guid Id PK
  }
  Sessions {
    guid Id PK
    guid MemberId FK "FK to Members"
  }
  SecurityKeys {
    guid Id PK
    guid MemberId FK "FK to Members"
  }
  Members ||--o{ KeyLinks : "MemberId"
  Members ||--o{ Sessions : "MemberId"
  Members ||--o{ SecurityKeys : "MemberId"
```

| Table | Columns | Primary key | Notes |
|---|---|---|---|
| `KeyLinks` | 8 | Id |  |
| `AuditEntries` | 10 | Id |  |
| `Members` | 11 | Id |  |
| `Sessions` | 8 | Id |  |
| `SecurityKeys` | 8 | Id |  |

<details><summary><code>platform.KeyLinks</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `ExpiresAt` | timestamptz |  |  |
| `IssuedAt` | timestamptz |  |  |
| `IssuedBy` | guid |  |  |
| `MemberId` | guid |  | FK to `PlatformMember` |
| `Purpose` | character varying(16) |  |  |
| `TokenHash` | bytes |  |  |
| `UsedAt` | timestamptz | yes |  |

</details>

<details><summary><code>platform.AuditEntries</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `Action` | character varying(64) |  |  |
| `ActorMemberId` | guid | yes |  |
| `ClientIp` | character varying(45) |  |  |
| `CorrelationId` | character varying(64) |  |  |
| `OccurredAt` | timestamptz |  |  |
| `Reason` | character varying(500) |  |  |
| `ResourceId` | guid | yes |  |
| `ResourceType` | character varying(64) |  |  |
| `SubjectTenantId` | guid | yes |  |

</details>

<details><summary><code>platform.Members</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `AccessEndsOn` | date | yes |  |
| `DisplayName` | character varying(100) |  |  |
| `Email` | character varying(254) |  |  |
| `InvitedAt` | timestamptz |  |  |
| `InvitedBy` | guid | yes |  |
| `Issuer` | character varying(256) |  |  |
| `LastActiveAt` | timestamptz | yes |  |
| `RemovedAt` | timestamptz | yes |  |
| `Role` | character varying(16) |  |  |
| `Subject` | character varying(256) |  |  |

</details>

<details><summary><code>platform.Sessions</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `CreatedAt` | timestamptz |  |  |
| `KeyLinkId` | guid | yes |  |
| `KeyVerifiedAt` | timestamptz | yes |  |
| `LastSeenAt` | timestamptz |  |  |
| `MemberId` | guid |  | FK to `PlatformMember` |
| `PendingCeremony` | character varying(8000) |  |  |
| `RevokedAt` | timestamptz | yes |  |

</details>

<details><summary><code>platform.SecurityKeys</code> columns</summary>

| Column | Type | Null | Links to |
|---|---|---|---|
| `Id` | guid |  |  |
| `CreatedAt` | timestamptz |  |  |
| `CredentialId` | bytes |  |  |
| `LastUsedAt` | timestamptz | yes |  |
| `MemberId` | guid |  | FK to `PlatformMember` |
| `PublicKey` | bytes |  |  |
| `RevokedAt` | timestamptz | yes |  |
| `SignCount` | bigint |  |  |

</details>

## 5. Whole application: built and planned

From `docs/03-architecture/data-model.md` (areas A to D). "Built" means a table exists in the snapshots; some are narrower than the plan.

| Area | Planned entity | Status | Built as |
|---|---|---|---|
| A · Tenant, practice, PMS, billing | `TENANT` | built | `tenancy.Tenants` |
| A · Tenant, practice, PMS, billing | `FACILITY` | built | `tenancy.Facilities` |
| A · Tenant, practice, PMS, billing | `DEPARTMENT` | not built | |
| A · Tenant, practice, PMS, billing | `USER` | built | `identity.Users` |
| A · Tenant, practice, PMS, billing | `MEMBERSHIP` | built | `tenancy.Memberships` |
| A · Tenant, practice, PMS, billing | `ROLE` | partly built | `tenancy.CustomRoles` (custom roles only; the built-in roles are in code (RoleMatrix)) |
| A · Tenant, practice, PMS, billing | `ROLE_PERMISSION` | not built | |
| A · Tenant, practice, PMS, billing | `CLINICIAN_PROFILE` | built | `tenancy.DoctorProfiles` |
| A · Tenant, practice, PMS, billing | `CLINICIAN_LICENCE` | built | `tenancy.DoctorLicences` |
| A · Tenant, practice, PMS, billing | `SCHEDULE_TEMPLATE` | built | `scheduling.Templates` |
| A · Tenant, practice, PMS, billing | `LEAVE` | partly built | `scheduling.ScheduleExceptions` (also holds extra sessions and shortened days) |
| A · Tenant, practice, PMS, billing | `HOLIDAY` | built | `tenancy.Closures` |
| A · Tenant, practice, PMS, billing | `PATIENT` | built | `patients.Patients` |
| A · Tenant, practice, PMS, billing | `PATIENT_IDENTIFIER` | built | `patients.Identifiers` |
| A · Tenant, practice, PMS, billing | `PATIENT_RELATIONSHIP` | built | `patients.Relationships` |
| A · Tenant, practice, PMS, billing | `PAYER_COVERAGE` | built | `patients.PayerCoverages` |
| A · Tenant, practice, PMS, billing | `CONSENT_RECEIPT` | partly built | `patients.ConsentReceipts` (registration consents; the per-encounter recording consent in area C is not built) |
| A · Tenant, practice, PMS, billing | `SCHEDULE_SLOT` | built | `scheduling.ScheduleSlots` |
| A · Tenant, practice, PMS, billing | `APPOINTMENT` | built | `scheduling.Appointments` |
| A · Tenant, practice, PMS, billing | `WAITLIST_ENTRY` | built | `scheduling.Waitlist` |
| A · Tenant, practice, PMS, billing | `QUEUE_TOKEN` | built | `scheduling.QueueTokens` |
| A · Tenant, practice, PMS, billing | `SERVICE_CATALOGUE` | not built | |
| A · Tenant, practice, PMS, billing | `PRICE_VERSION` | not built | |
| A · Tenant, practice, PMS, billing | `PACKAGE` | not built | |
| A · Tenant, practice, PMS, billing | `INVOICE` | not built | |
| A · Tenant, practice, PMS, billing | `INVOICE_LINE` | not built | |
| A · Tenant, practice, PMS, billing | `PAYMENT` | not built | |
| A · Tenant, practice, PMS, billing | `PAYMENT_SPLIT` | not built | |
| A · Tenant, practice, PMS, billing | `DOC_SEQUENCE` | partly built | `billing.DocSequences` (numbering only; invoices and payments are not built) |
| A · Tenant, practice, PMS, billing | `PACKAGE_ENROLMENT` | not built | |
| A · Tenant, practice, PMS, billing | `REDEMPTION` | not built | |
| A · Tenant, practice, PMS, billing | `DUE` | not built | |
| A · Tenant, practice, PMS, billing | `STATEMENT` | not built | |
| A · Tenant, practice, PMS, billing | `CASH_SESSION` | not built | |
| A · Tenant, practice, PMS, billing | `CLAIM` | not built | |
| A · Tenant, practice, PMS, billing | `ELIGIBILITY_CHECK` | not built | |
| A · Tenant, practice, PMS, billing | `PRIOR_AUTH` | not built | |
| A · Tenant, practice, PMS, billing | `REMITTANCE` | not built | |
| A · Tenant, practice, PMS, billing | `CLAIM_RESUBMISSION` | not built | |
| B · EMR, clinical authority | `ENCOUNTER` | built | `encounters.Encounters` |
| B · EMR, clinical authority | `OBSERVATION` | built | `encounters.Observations` |
| B · EMR, clinical authority | `PROBLEM` | built | `encounters.Problems` |
| B · EMR, clinical authority | `ALLERGY` | built | `encounters.Allergies` |
| B · EMR, clinical authority | `MEDICATION_STATEMENT` | built | `encounters.MedicationStatements` |
| B · EMR, clinical authority | `CLINICAL_NOTE` | not built | |
| B · EMR, clinical authority | `NOTE_VERSION` | not built | |
| B · EMR, clinical authority | `CLINICIAN_SIGNATURE` | not built | |
| B · EMR, clinical authority | `CLINICAL_ORDER` | built | `prescribing.Orders` |
| B · EMR, clinical authority | `CLINICAL_RESULT` | not built | |
| B · EMR, clinical authority | `PRESCRIPTION` | not built | |
| B · EMR, clinical authority | `PRESCRIPTION_ITEM` | not built | |
| B · EMR, clinical authority | `REFERRAL` | built | `prescribing.Referrals` |
| B · EMR, clinical authority | `PHARMACY_DISPATCH` | not built | |
| B · EMR, clinical authority | `FORMULARY_ITEM` | built | `prescribing.FormularyItems` |
| B · EMR, clinical authority | `ORDER_SET` | built | `prescribing.OrderSets` |
| B · EMR, clinical authority | `CLINICAL_DOCUMENT` | built | `encounters.ClinicalDocuments` |
| B · EMR, clinical authority | `DOCUMENT_PROVENANCE` | not built | |
| B · EMR, clinical authority | `EXTERNAL_RECORD_LINK` | not built | |
| C · Consent, capture, AI | `CONSENT_RECEIPT` | not built | |
| C · Consent, capture, AI | `ENCOUNTER_PARTICIPANT` | not built | |
| C · Consent, capture, AI | `DEVICE` | not built | |
| C · Consent, capture, AI | `DEVICE_PAIRING` | not built | |
| C · Consent, capture, AI | `RECORDING_SESSION` | not built | |
| C · Consent, capture, AI | `AUDIO_CHUNK` | not built | |
| C · Consent, capture, AI | `TRANSCRIPT` | not built | |
| C · Consent, capture, AI | `TRANSCRIPT_SEGMENT` | not built | |
| C · Consent, capture, AI | `SPEAKER_LABEL` | not built | |
| C · Consent, capture, AI | `AI_JOB` | not built | |
| C · Consent, capture, AI | `AI_RUN` | not built | |
| C · Consent, capture, AI | `NOTE_DRAFT` | not built | |
| C · Consent, capture, AI | `EVIDENCE_LINK` | not built | |
| D · Care, portal, integration, audit | `CARE_PLAN` | not built | |
| D · Care, portal, integration, audit | `CARE_TASK` | not built | |
| D · Care, portal, integration, audit | `CARE_TASK_EVENT` | not built | |
| D · Care, portal, integration, audit | `INBOX_ITEM` | not built | |
| D · Care, portal, integration, audit | `REVIEW_ACKNOWLEDGEMENT` | not built | |
| D · Care, portal, integration, audit | `MESSAGE_THREAD` | not built | |
| D · Care, portal, integration, audit | `MESSAGE` | not built | |
| D · Care, portal, integration, audit | `NOTIFICATION` | partly built | `notifications.Notifications` (patient messages only; staff notifications are separate tables) |
| D · Care, portal, integration, audit | `NOTIFICATION_ATTEMPT` | built | `notifications.Attempts` |
| D · Care, portal, integration, audit | `PORTAL_ACCESS` | not built | |
| D · Care, portal, integration, audit | `PORTAL_PROXY` | not built | |
| D · Care, portal, integration, audit | `INTEGRATION_CONNECTION` | not built | |
| D · Care, portal, integration, audit | `EXTERNAL_PATIENT_MAP` | not built | |
| D · Care, portal, integration, audit | `EXTERNAL_RESOURCE_MAP` | not built | |
| D · Care, portal, integration, audit | `INTEGRATION_MESSAGE` | not built | |
| D · Care, portal, integration, audit | `DELIVERY` | not built | |
| D · Care, portal, integration, audit | `INTEGRATION_ATTEMPT` | not built | |
| D · Care, portal, integration, audit | `AUDIT_EVENT` | built | `audit.AuditEvents` |
| D · Care, portal, integration, audit | `OUTBOX_EVENT` | built | `scheduling.OutboxEvents` |

Tables that exist but were added while building (not named in the plan): `encounters.ConsultNotes`, `encounters.DoctorNoteTemplates`, `encounters.IntakeEscalations`, `encounters.Intakes`, `encounters.MedicationDecisions`, `encounters.MedicationReconciliations`, `encounters.SignedNoteVersions`, `identity.OnboardingDrafts`, `identity.Sessions`, `leads.DemoRequests`, `leads.Waitlist`, `notifications.Preferences`, `notifications.StaffNotificationReads`, `notifications.StaffNotifications`, `patients.DataKeys`, `patients.MergeRequests`, `platform.AuditEntries`, `platform.KeyLinks`, `platform.Members`, `platform.SecurityKeys`, `platform.Sessions`, `prescribing.ReferralEvents`, `scheduling.AppointmentReschedules`, `scheduling.BookingConflicts`, `scheduling.MetricBaselines`, `scheduling.OutboxTenants`, `scheduling.QueueDisplays`, `scheduling.Resources`, `scheduling.SlotHolds`, `scheduling.VisitStatusEvents`, `tenancy.AccessRequests`, `tenancy.AccessReviewDecisions`, `tenancy.AccessReviews`, `tenancy.FeatureFlags`, `tenancy.Invitations`, `tenancy.OperatingHours`, `tenancy.OrganisationDocuments`, `tenancy.OrganisationRequests`, `tenancy.PracticeDefaults`, `tenancy.Profiles`, `tenancy.SettingsChanges`, `tenancy.TenantIndex`, `tenancy.TermsAcceptances`.

**Counts:** 77 tables across 11 schemas; 34 of 90 planned entities are built; 43 tables were added during the build.

## 6. Rules the model follows

- **Tenant isolation:** 67 of 77 tables carry `TenantId` (EF query filter plus PostgreSQL forced row-level security on `medvya.tenant_id`). Without `TenantId`: `identity.OnboardingDrafts`, `identity.Users`, `leads.DemoRequests`, `leads.Waitlist`, `platform.AuditEntries`, `platform.KeyLinks`, `platform.Members`, `platform.SecurityKeys`, `platform.Sessions`, `tenancy.Tenants`. (`TenantId` present is not proof the row-level-security policy covers the table; see `docs/03-architecture/database-security.md`.)
- **Append-only ledgers:** `audit.AuditEvents`, `encounters.SignedNoteVersions`, `patients.ConsentReceipts`, `tenancy.TermsAcceptances` (hash-chained, MDV-P0-036; each also has ChainSeq, PrevHash and RowHash added by `LedgerSql`, outside the EF model).
- **No cross-module foreign keys:** modules link by id and through application services (AGENTS.md 3.1).
- **Country data plane:** one deployment and one database per country (ADR-003); this diagram is the same in each.
- **Money** is integer minor units plus an ISO currency; **time** is `timestamptz` in UTC (AGENTS.md 3.4, 3.5).

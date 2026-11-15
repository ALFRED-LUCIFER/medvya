# Runbook: put the platform console on Azure dev

The platform console (`src/Host.Platform` serving `web/platform-admin`, ADR-014, MDV-P5-040) runs as its own web app beside the clinic app, in the same resource group, region (Central India), App Service plan and VNet. The template, the database job and the deploy workflow do the Azure work; a person does the Entra and GitHub steps below, once, in this order.

## What the template makes

| Thing | Name | What it may do |
|---|---|---|
| Web app | `console-medvya-dev-<suffix>` | HTTPS only, TLS 1.2+, FTP off, health check `/health`, VNet integration to the private database. Same `<suffix>` as the clinic app `app-medvya-dev-<suffix>`. |
| Its system identity | (the web app's) | Database login `console-medvya-dev-<suffix>` **in role `medvya_platform` only** (schema `platform`, no clinic schema). Reads the one Key Vault secret `platform-console-client-secret`. Reads organisation documents in `practice-assets` (Blob Data Reader, plus Storage Blob Delegator for the five-minute read link). Sends the approval emails through Azure Communication Services. |
| Identity `id-review-medvya-dev-<suffix>` | user-assigned, on the console | Database login of the same name **in role `medvya_review` only** (the organisation approvals, MDV-P1-064). The connection string names its client id (`Entra Client Id=…`), so this login never shares a token with the console's own login. |
| Identity `id-features-medvya-dev-<suffix>` | user-assigned, on the console | Database login of the same name **in role `medvya_features` only** (`tenancy.FeatureReleases`, the console's feature releases, MDV-P5-041), set as `ConnectionStrings__Features__IN`. The UAE deployment sets its own `:AE`. |
| Identity `id-formulary-medvya-dev-<suffix>` | user-assigned, on the console | Database login of the same name **in role `medvya_formulary` only** (the Medvya-wide medicine list: rows with no tenant in `prescribing.FormularyItems`, MDV-P5-064), set as `ConnectionStrings__Formulary`. |
| Settings | on the console | `Authentication__Oidc__{Authority,ClientId,ClientSecret}` (the secret is a Key Vault reference), `Platform__RpId` and `Platform__Origins__0` (the console's own host), `Platform__BootstrapOwner__{Issuer,Subject}`, `ConnectionStrings__Platform`, `ConnectionStrings__Review`, `ConnectionStrings__Features__IN`, `ConnectionStrings__Formulary`, `Storage__BlobEndpoint`, the `Email__*` settings of the clinic app. No password anywhere. |

The deploy job (`job-db-dev-<suffix>`) maps the four logins on every deploy, after `roles.sql`, with the same `db-migrate entra` step as the clinic app. The Platform migrations already run in it (schema `platform` of database `medvya`; the console gets its own database with MDV-P5-017).

Not wired: the clinic API's `Features:InternalOrganisations` (Medvya's own test organisations, the Internal release stage) is not set by the template. The console sends no telemetry to Application Insights yet (the scrubber lives in `Host.Api`); App Service's own logs are all there is.

## Steps, in order

You need: an Application Administrator in the company Entra tenant, Key Vault Secrets Officer on `kv-medvya-dev-<suffix>`, and admin on the GitHub repository. `<suffix>` is the end of the clinic web app's name (variable `AZURE_WEBAPP_NAME`).

1. **Register the console in Entra and store its secret.** Signed in to the company tenant:

   ```
   az login --tenant <tenant id>
   infra/entra/console-app.sh <tenant id> console-medvya-dev-<suffix>.azurewebsites.net kv-medvya-dev-<suffix>
   ```

   It creates (or finds) the single-tenant app registration "Medvya platform console (<host>)" with one web redirect URI `https://<host>/signin-oidc`, no implicit flow, its service principal, and a one-year client secret written straight into Key Vault as `platform-console-client-secret`. It prints `CONSOLE_SIGNIN_CLIENT_ID=<app id>`. Record the run (date, tenant, who) in `docs/05-delivery/session-handoff.md`.

2. **Set the repository variables** on both GitHub environments `dev` and `dev-infra` (Settings → Environments). They are Entra ids, not secrets:

   | Variable | Value |
   |---|---|
   | `CONSOLE_SIGNIN_CLIENT_ID` | the id step 1 printed |
   | `CONSOLE_OWNER_OBJECT_ID` | the first owner's Entra object id: signed in as that person, `az ad signed-in-user show --query id -o tsv` |

   From now on `infra-dev` refuses to run without both variables and without the secret of step 1.

3. **Run `infra-dev`** (Actions → infra-dev → Run workflow, from `main`). Read the what-if before approving `dev-infra`: it adds the console web app, the `id-review-*`, `id-features-*` and `id-formulary-*` identities, four role assignments (the secret, `practice-assets`, the storage account delegator, Communication Services) and the console's four login steps in the database job. Nothing on the clinic app changes.

4. **Set `AZURE_CONSOLE_WEBAPP_NAME`** on the `dev` environment to `console-medvya-dev-<suffix>` (output `consoleName` of the deployment). Until this variable exists, `deploy-dev` skips the console.

5. **Run `deploy-dev`** from `main` (`gh workflow run deploy-dev.yml --ref main`). After the database job (which now also maps the four console logins) and the clinic app, it deploys the console and smoke-tests it: `/health` reports the new version, `/` serves the console, `/v1/me` without a session answers 401, and `/bff/login` redirects to Entra with the `https://<host>/signin-oidc` callback.

6. **Sign in as the first owner.** Open `https://console-medvya-dev-<suffix>.azurewebsites.net`, choose "Continue with company sign-on" and sign in with the account whose object id is `CONSOLE_OWNER_OBJECT_ID`. Entra may ask once for consent to sign you in and read your profile. The console creates you as owner because the team is empty (`team.bootstrap` in the audit log); nobody else can sign on yet.

7. **Register your security key.** The console asks for one at once (allowed without a link only while no key exists). Use a **hardware security key** (FIDO2, with a PIN) or a **device-bound passkey** (Windows Hello, or a platform passkey that does not sync). **Synced passkeys are refused by design** (iCloud Keychain, Google Password Manager, 1Password and the like report themselves as backup-eligible): in the browser's passkey prompt choose "security key" or "another device", not the password manager. Any later key needs a link from another owner, so do the next step soon.

8. **Invite the rest of the team.** Team access → Invite: name, work email, role (owner, operations, support, finance, reviewer) and a reason. The console shows a single-use link valid for one hour; send it over a channel you trust. The invitee opens it, signs on with their own company account (same tenant) and registers their own key. Invite a second owner first: until one exists, a lost key locks the console (recovery needs another owner).

## Later

- **Secret rotation:** the client secret expires after one year. Run step 1 again (it replaces the secret), then restart the console web app.
- **Bootstrap owner:** `Platform:BootstrapOwner` is ignored once the team has a member; leave the variable as it is. The owner's name and address are not configured: each sign-on takes them from the company sign-on (the `name` claim, and `email` or `preferred_username`), for every member, so Team access and the audit log name the person. A first key must be a hardware security key (USB or NFC): a passkey that syncs between devices is refused, and the sign-in page says so.
- **Lost key:** another owner verifies the person on a video call and issues a recovery link (Team access); nobody recovers their own key.

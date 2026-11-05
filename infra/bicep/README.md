# infra/bicep: India dev environment (MDV-P0-006)

`main.bicep` is the cheapest viable dev environment in Central India. It is for synthetic data only; the `shortcut:` comment at the top lists what is deliberately missing.

```
az group create -n rg-medvya-dev-in -l centralindia
az deployment group what-if -g rg-medvya-dev-in -f infra/bicep/main.bicep -p sqlAdminObjectId=<entra object id> sqlAdminLogin=<upn> alertEmail=<email> oidcClientId=<app registration client id> emailFrom=<verified sender>
az deployment group create  -g rg-medvya-dev-in -f infra/bicep/main.bicep -p <same parameters>
```

- Sign-in is a single-tenant Entra app registration. Its client secret lives only in Key Vault (`oidc-client-secret`); the web app reads it through a Key Vault reference.
- Cost guard: a monthly budget (default 1,800 INR) mails alerts at 50/80/100 %; Log Analytics is capped at 0.1 GB/day.
- No secret value belongs in this folder.

## Web app (MDV-P0-007)

The API serves the built clinic app from `wwwroot/app` under `/app/` (the public site keeps `/`), so the strict session cookie needs no CORS. The deploy workflow builds `web/clinic` (Vite base `/app/`) and copies `dist` into the publish folder. Settings that point server-made links there: `Web__AppPath=/app/`, `Invitations__LinkBaseUrl`, `PublicSite__Deployments__SignUpUrls__IN`.

## Manual Entra settings (not in Bicep)

Staff sign-in needs both, or the app refuses every sign-in with "the provider did not report multi-factor authentication" (MDV-P0-011):

1. **MFA enforced for the user.** Free tier: Security defaults off, then Per-user MFA = Enforced (Conditional Access needs a paid licence). Production moves staff sign-in to an Entra External ID tenant.
2. **`amr` optional claim on the app registration** (Token configuration → Add optional claim → ID → `amr`). v2.0 ID tokens omit it otherwise. CLI: `az ad app update --id <client id> --optional-claims '{"idToken":[{"name":"amr","essential":false,"additionalProperties":[]}]}'`.

## Email (MDV-P1-032)

Invitations (add doctor, add staff, onboarding) go through SendGrid's SMTP relay. One-time setup, by the owner:

1. SendGrid: verify a sender (a domain you own is best; a free-mail address often fails DMARC) and create an API key with only "Mail Send".
2. Store the key without echoing it: `az keyvault secret set --vault-name <kv> --name sendgrid-api-key --value "<key>"`.
3. Deploy with `emailFrom=<the verified sender>`.

The mailer fails closed: no sender or key means the request fails and nothing is saved.


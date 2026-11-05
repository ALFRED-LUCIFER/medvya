# infra/bicep: India dev environment (MDV-P0-006)

`main.bicep` is the cheapest viable dev environment in Central India. It is for synthetic data only; the `shortcut:` comment at the top lists what is deliberately missing.

```
az group create -n rg-medvya-dev-in -l centralindia
az deployment group what-if -g rg-medvya-dev-in -f infra/bicep/main.bicep -p sqlAdminObjectId=<entra object id> sqlAdminLogin=<upn> alertEmail=<email> oidcClientId=<app registration client id>
az deployment group create  -g rg-medvya-dev-in -f infra/bicep/main.bicep -p <same parameters>
```

- Sign-in is a single-tenant Entra app registration. Its client secret lives only in Key Vault (`oidc-client-secret`); the web app reads it through a Key Vault reference.
- Cost guard: a monthly budget (default 1,800 INR) mails alerts at 50/80/100 %; Log Analytics is capped at 0.1 GB/day.
- No secret value belongs in this folder.

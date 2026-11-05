# Email providers (MDV-P1-032)

Transactional email (invitations, confirmations) goes through `IEmailSender`. `Email:Provider` picks the implementation; the choice is configuration, so it can change without a code change. Email is not a PHI channel: no patient data in any message.

| `Email:Provider` | Sender | Settings | Notes |
|---|---|---|---|
| `Smtp` (default) | `SmtpEmailSender` | `Email:Smtp:Host`, `Port`, `EnableSsl`, `UserName`, `Password`, `From` | smtp4dev locally; SendGrid's relay in the cloud (`smtp.sendgrid.net:587`, user `apikey`, password = API key from Key Vault). Needs a verified sender. |
| `Acs` | `AcsEmailSender` | `Email:Acs:Endpoint` (the Communication Services resource address), `Email:Acs:From` (a sender of its linked domain) | Azure Communication Services Email with the app's managed identity: no key or password. The app identity needs the role "Communication and Email Service Owner" on the resource. An Azure-managed domain gives a ready sender without owning a domain. |

Both fail closed: missing settings mean nothing is sent and the caller's request fails (invitations roll back). An unknown provider name stops the app at start-up.

Switching to Acs: create the Communication Services and Email Services resources with an Azure-managed domain (register the `Microsoft.Communication` provider first), give the app identity the role, then set `Email__Provider=Acs`, `Email__Acs__Endpoint`, `Email__Acs__From`. The Bicep (`infra/bicep/main.bicep`) creates the Email Service, the Azure-managed domain, the Communication Service (data location parameter `emailDataLocation`, default India), the role for the app identity, and these three settings. Register the provider once: `az provider register -n Microsoft.Communication`.

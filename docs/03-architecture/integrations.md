# External integrations

Every integration sits behind an adapter in `Integrations` or `Notifications`, uses a least-privilege identity with secrets in Key Vault, is idempotent, and records attempts. Regulator onboarding dates are outside our control (PRD v2 risk: HIGH).

| Integration | Purpose | Phase | Country | Notes / dependency |
|---|---|---|---|---|
| Microsoft Entra External ID | Staff and patient sign-in, MFA, passkeys | P0 | Both | BFF keeps tokens server-side. Hospital federation (SAML/OIDC) and SCIM in P7 |
| Azure Communication Services — email | Confirmations, reminders, invites | P1 | Both | Templates without clinical details |
| SMS | Confirmations, reminders, OTP | P1 | Both | India: **DLT sender ID and templates must be registered** (P1 dependency) |
| WhatsApp Business API | Reminders, confirmations, one-tap reschedule, running-late broadcast | P4 | Both | **Template approval** dependency. Opt-in records per channel |
| Razorpay | UPI, card, refunds, webhooks | P4 | India | Merchant approval can take weeks (apply during P2) |
| Network International or Stripe | Card payments | P4 | UAE | Same |
| Card terminal (e.g. Pine Labs) | Card on terminal | P4 | India | No card data stored |
| ABDM (ABHA verification, Scan & Share) | Registration demographics with consent | P1 (manual entry) → P8 (consent records) | India | P1 stores ABHA as optional, entered manually. Full HIP/HIU consent flows in P8-03, certified via the ABDM sandbox |
| Emirates ID card reader / UAE PASS | Check-in < 30 s | P5 | UAE | Hardware sourcing dependency. P1 enters EID manually |
| Malaffi / NABIDH / Riayati | HIE pull at check-in, push of signed visits | P5 (launch emirate), P8 (the other two) | UAE | HL7 v2/FHIR after regulator onboarding and conformance testing |
| eClaimLink / Shafafiya | Eligibility, pre-auth, claims, remittance | P5 | UAE | Regulator onboarding |
| TPA / PMJAY / CGHS | Pre-auth, claims, remittance | P5 | India | Mostly portal/email-driven. Medvya records references and does not decide eligibility |
| Regulator e-prescription and e-sick leave | eRx submission, sick leave certificates | P5 | UAE | Format per emirate |
| Azure AI Speech + Azure OpenAI | Transcription, drafting | P3 | Both | Confirm model availability in Central India and UAE North during P0 |
| Azure Document Intelligence | Patient upload extraction | P5 | Both | Through the AI worker queue. The original is always kept |
| Hospital HIS (FHIR R4 via Firely SDK) | Read, and write-back of signed content | P7 | Both | Write-back queue, conflict and retry, receipts |
| LIS (HL7 v2 ORU/ORM, MLLP over VPN) | Lab orders out, results in | P7 (N-15) | Both | Before P7, results arrive via upload (C-05 matching) |
| Imaging (DICOMweb viewer links) | Open images in the provider viewer | P7 | Both | Link only, never copy |
| Legacy DB migration (Oracle/SQL Server/MySQL/CSV) | Day-one load + CDC parallel run | P5 | Both | Azure Data Factory self-hosted IR (no open ports). Debezium/LogMiner or GoldenGate for CDC |
| Azure Communication Services — voice | Voice appointment assistant | P5 (inbound), P6 (outbound) | Both | SIP trunk to the clinic line. Speech in-region. Books through the same PMS API |

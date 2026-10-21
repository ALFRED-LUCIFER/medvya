# Record-retention map per country (MDV-P0-024)

Proposed defaults. **Every row needs counsel confirmation (⚖) before real data of that class** (P1 demographics, P2 clinical, P3 audio). Retention is enforced by scheduled jobs (MDV-P3-019, MDV-P5-025). A legal hold always blocks deletion. Deletions are logged without PHI.

| Data class | India default | UAE default | Basis to confirm ⚖ | Enforced by |
|---|---|---|---|---|
| Consultation audio | 30 days, then delete | 30 days, then delete | Purpose limitation (documentation only); recording consent wording | MDV-P3-019 |
| Transcripts and AI drafts | Same as the medical record (kept with the signed note) | Same as the medical record | Evidence for the signed note | MDV-P5-025 |
| Medical records (notes, Rx, results, documents) | Configured with counsel; commonly ≥ 3 years for OPD records, longer for minors and medico-legal cases | Configured with counsel per DoH/DHA rules; commonly long-term (e.g. 25 years) | National/state rules; medical council regulations; DoH/DHA standards | MDV-P5-025 |
| Patient demographics and identifiers | Life of the medical record | Life of the medical record | Linked to record retention | MDV-P5-025 |
| Consent receipts | Life of the record + limitation period | Same | Proof of consent | Ledger (no deletion while the record exists) |
| Billing, invoices, receipts | 8 years (tax) | 5 years (VAT) | Income-tax / GST rules; UAE VAT law | MDV-P5-025 |
| Audit events | 7 years (configurable) | 7 years (configurable) | Security and accountability | Ledger + Log Analytics retention |
| Application logs/telemetry (PHI-free) | 90 days | 90 days | Operational need | Log Analytics workspace setting (MDV-P0-006) |
| Staff accounts | Deactivate on offboarding; delete profile after 2 years unless referenced by audit | Same | Contract | MDV-P1-009 |
| Demo requests (public site) | 2 years after the last contact, then delete | Same | Purpose limitation (sales follow-up) | Not yet automated (MDV-P5-025) |
| Backups | PITR 35 days; LTR per country rule; in-country only | Same | Business continuity | MDV-P0-019 |

> These figures are planning defaults from the design sources (H-06: audio 30 days, audit 7 years) plus common practice. They are **not legal advice** and must not be presented to customers as compliance claims.

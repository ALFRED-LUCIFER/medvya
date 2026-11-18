# Intake · MDV-P8-E02 Dental clinic pack

**Status: Draft, awaiting the founder's approval.** Until approved, dental is built behind the `clinical.dental` feature switch at the Internal release
stage only (Medvya's own test organisations); no customer clinic can switch it on.

## 1 · Adoption target

- **Who asked:** dental practices and mixed GP and dental clinics in the India pilot pipeline, who keep the tooth chart and treatment plan on paper.
- **Target:** at least one dental chair running a full clinic day in Medvya at the pilot (MDV-P8-022), then 10 dental or mixed clinics within six months of
  release.
- **Success measure:** the dentist records chart findings and the plan in Medvya for at least 90 % of treatment visits in the pilot month, and every done
  procedure reaches the visit's bill without being typed again.

## 2 · Evidence

- **Demand:** the dental feature gap analysis against DentalERP (abinauv/dental-erp, MIT, reviewed 17 Nov 2026) and the Medvya Dental design canvas
  (DN-01 to DN-14) built with the founder.
- **Practice standard:** FDI two-digit tooth notation (ISO 3950), five-surface charting (M, D, O, B, L), phased treatment plans with a written estimate the
  patient accepts, procedure consent, sterilisation records (Dental Council of India and state rules; UAE DHA/DOH infection-control standards).

## 3 · Regulatory classification

| Part | Decision support? | Classification |
|---|---|---|
| Tooth chart, history, treatment plan, estimate, acceptance, billing | No: records what the dentist decides | Record keeping, outside medical-device scope |
| Dental licences, chairs and booking, recall reminders, stock | No | Administrative |
| Note template and dental medicines in the formulary | No: the list is curated, the dentist chooses | Record keeping; Schedule class shown per ADR-015 |
| Medical history check before treatment (P8-014) | Shows the dentist's own recorded answers as a warning in words; no score, no recommendation | Treated as record keeping. **A regulatory adviser confirms this before release.** |
| Procedure consent | No | Record keeping; wording approved by a lawyer and a dentist |

No part diagnoses, scores risk or suggests a treatment. Any future AI feature in the pack gets its own intake record.

## 4 · Needs a person before release

- A dentist: the condition list and surfaces (`DentalRules.SignOff`), the 76 fees, the history-check questions, the note template, the dental medicine
  list, the procedure stages, and the 18 open questions on the design canvas.
- A chartered accountant: GST on dental services (MDV-P4-031).
- A lawyer and a dentist: consent wording (MDV-P8-019).
- A regulatory adviser: section 3, history check.
- A native speaker: Hindi and Arabic strings.

## 5 · Approval

| Role | Name | Date |
|---|---|---|
| Founder (product) | — | — |

# P8 feature intake (MDV-P8-001)

Every P8 feature is built only after an intake record in this folder is approved (MDV-P8-001 AC1). One record per epic or feature, named after it
(`MDV-P8-E02-dental-pack.md`). Each P8 ticket links its record in its notes.

A record answers, in plain words:

1. **Adoption target**: who asked, how many clinics are expected to use it, and the number that would make it a success.
2. **Evidence**: customer demand (named, synthetic in this repo) and clinical evidence or practice standard behind it.
3. **Regulatory classification**: does any part give clinical decision support? If so, its class under the Indian Medical Devices Rules 2017 and the
   UAE MOHAP rules, and what keeps it outside or inside medical-device scope. Pure record keeping is not decision support.
4. **What needs a person**: the clinician, lawyer or accountant sign-offs the feature waits for before release.
5. **Approval**: who approved it and when. Until then the record says **Draft**, and work may be built behind its feature switch at the Internal release
   stage only; it never reaches a customer.

Released features get minor-version release notes from [docs/releases/minor-version-template.md](../../releases/minor-version-template.md) (AC2).

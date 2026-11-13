import { PatientBanner, type BannerClinical } from '@medvya/ui';

// MDV-P1-081: every variant, with one, three and five allergies, the two other allergy states and a long name. Synthetic patients only.
const allergies = [
  { substance: 'Penicillin', reaction: 'rash' }, { substance: 'Sulfonamides' }, { substance: 'Aspirin', reaction: 'wheeze' },
  { substance: 'Latex' }, { substance: 'Peanut', reaction: 'swelling' },
] as const;
const recorded = (n: number): BannerClinical['allergy'] => ({ state: 'recorded', allergies: [allergies[0], ...allergies.slice(1, n)] });
const kavya: BannerClinical = { name: 'Kavya Reddy', age: 54, sex: 'Female', mrn: 'DQ-10-4482', dob: '12 Mar 1972', allergy: recorded(1), consent: 'given', lifeStage: 'Adult', careTeam: 'Dr. Arjun Rao' };
const longName: BannerClinical = { name: 'Venkata Lakshmi Narasimha Subramaniam Ramachandran-Iyengar', age: 71, sex: 'Male', mrn: 'DQ-12-0917', dob: '30 Jun 1955', allergy: recorded(5), consent: 'given' };

export default function PatientBannerSection() {
  return (
    <section aria-labelledby="g-banner" className="flex flex-col gap-3">
      <h2 id="g-banner" className="text-heading">Patient banner</h2>
      <h3 className="text-subheading">full</h3>
      <PatientBanner variant="full" patient={kavya} />
      <PatientBanner variant="full" patient={{ ...kavya, name: 'Rahul Varma', sex: 'Male', mrn: 'DQ-11-1093', allergy: { state: 'noneKnown' }, careTeam: undefined }} />
      <h3 className="text-subheading">strip</h3>
      <PatientBanner variant="strip" patient={longName} token="Token A-07" room="Room 3" />
      <PatientBanner variant="strip" patient={{ ...kavya, name: 'Arjun Menon', sex: 'Male', mrn: 'DQ-11-2210', allergy: recorded(3) }} token="Token A-08" />
      <PatientBanner variant="strip" patient={{ name: 'Noura Al Hashimi', age: 38, sex: 'Female', mrn: 'DQ-AE-0458', dob: '03 Feb 1988', allergy: { state: 'notRecorded' }, consent: 'notRecorded' }} />
      <h3 className="text-subheading">strip, pinned (in its own scroll box)</h3>
      <div role="region" aria-label="Pinned strip example" tabIndex={0} data-testid="pinned-box" className="max-h-64 overflow-y-auto rounded-lg border">
        <PatientBanner variant="strip" patient={{ ...kavya, name: 'Priya Raman', mrn: 'DQ-10-7731', allergy: recorded(3) }} room="Room 2" pinned />
        <div className="flex flex-col gap-3 p-4">{['History', 'Examination', 'Assessment', 'Plan', 'Follow-up', 'Notes'].map(s => <p key={s} className="min-h-16">{s}</p>)}</div>
      </div>
      <h3 className="text-subheading">desk</h3>
      <PatientBanner variant="desk" patient={{ name: 'Lakshmi Pillai', age: 29, sex: 'Female', mrn: 'DQ-10-5529', dob: '08 Jan 1997' }} />
    </section>
  );
}

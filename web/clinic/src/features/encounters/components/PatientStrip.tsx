import { CircleAlert, CircleCheck, DoorOpen } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, PatientBanner } from '@medvya/ui';
import { bannerAllergyState } from '../../patient360/domain/chart';
import type { EncounterBanner } from '../domain/encounter';

export type IdentityConfirmation = { canConfirm: boolean; confirming: boolean; confirmFailed: boolean; onConfirm: () => void };

/**
 * Who the encounter is for (MDV-P2-001), pinned under the top bar on the consultation and its sub-pages (MDV-P1-092), so no note or order
 * can land on the wrong person while the page scrolls. The shared PatientBanner strip: allergy and consent in words with an icon, never
 * colour alone; not recorded, no known allergies and listed are three answers. Identity confirmation, start time and room sit in its room slot.
 * MDV-P1-091: "Confirmed with the patient" is the doctor's own tap, stored by the server with who and when; the button sits under the strip,
 * in the page, so the pinned strip stays one short row.
 * shortcut: the life stage joins it when MDV-P2-032 adds it to the encounter.
 */
export default function PatientStrip({ banner, identity, headingLevel }: { banner: EncounterBanner; identity?: IdentityConfirmation; headingLevel?: 1 | 2 }) {
  const { t, formatDate, formatTime, formatCalendarDate } = useI18n();
  const { patient, encounter, timeZone } = banner;
  const started = new Date(encounter.startedAt);
  const when = { date: formatDate(started, timeZone), time: formatTime(started, timeZone) };
  return (
    <>
      <PatientBanner variant="strip" pinned headingLevel={headingLevel}
        patient={{
          name: patient.fullName, age: patient.ageYears, sex: patient.sexAtBirth, mrn: patient.mrn, dob: formatCalendarDate(patient.dateOfBirth),
          allergy: bannerAllergyState({ allergyState: banner.allergies.state, allergies: banner.allergies.allergies }, s => t(`patient360.allergyStatus.${s}`)),
          consent: banner.treatmentConsent ? 'given' : 'notRecorded',
        }}
        room={<>
          <span className="inline-flex items-center gap-1.5">
            {encounter.identityConfirmedAt
              ? <><CircleCheck aria-hidden className="size-4" />{t('consult.banner.identityConfirmed', { time: formatTime(new Date(encounter.identityConfirmedAt), timeZone) })}</>
              : <><CircleAlert aria-hidden className="size-4" />{t('consult.banner.identityNotConfirmed')}</>}
          </span>
          <span className="inline-flex items-center gap-1.5 text-muted-foreground"><DoorOpen aria-hidden className="size-4" />
            {encounter.room ? t('consult.banner.when', { ...when, room: encounter.room }) : t('consult.banner.whenNoRoom', when)}</span>
        </>} />
      {!encounter.identityConfirmedAt && identity?.canConfirm && (
        <div className="flex flex-wrap items-center gap-3">
          <Button variant="outline" loading={identity.confirming} aria-describedby="identity-confirm-hint" onClick={identity.onConfirm}>{t('consult.banner.confirmIdentity')}</Button>
          <span id="identity-confirm-hint" className="text-sm text-muted-foreground">{t('consult.banner.confirmIdentityHint')}</span>
          {identity.confirmFailed && <span role="alert" className="font-bold text-danger-700">{t('consult.banner.confirmFailed')}</span>}
        </div>
      )}
    </>
  );
}

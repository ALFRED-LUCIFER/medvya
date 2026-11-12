import { CircleAlert, CircleCheck, CircleHelp, DoorOpen } from 'lucide-react';
import { ltr, useI18n } from '@medvya/i18n';
import { allergyStatusOf, bannerAllergies } from '../../patient360/domain/chart';
import type { EncounterBanner } from '../domain/encounter';

/**
 * The identity banner (MDV-P2-001) at the top of every clinical screen of an encounter: who it is for, so no note or order can land on the
 * wrong person. Allergy and consent state are words with an icon, never colour alone.
 * Allergies are the chart's: listed (with each status), no known allergies, or not recorded. Unknown is never worded as "none".
 * shortcut: the one-page consultation's patient strip (MDV-P2-041) is this banner; the life stage joins it when MDV-P2-032 adds it to the encounter.
 */
export default function IdentityBanner({ banner }: { banner: EncounterBanner }) {
  const { t, formatDate, formatTime, formatCalendarDate } = useI18n();
  const { patient, encounter, timeZone } = banner;
  const started = new Date(encounter.startedAt);
  const allergies = bannerAllergies({ allergyState: banner.allergies.state, allergies: banner.allergies.allergies });
  return (
    <section aria-label={t('consult.banner.label')} className="flex flex-col gap-2 rounded-xl border-2 border-brand-700 bg-card p-4">
      <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
        <h1 className="text-xl font-extrabold">{patient.fullName}</h1>
        <span>{t('consult.banner.ageSex', { age: patient.ageYears, sex: t(`patients.sex.${patient.sexAtBirth}`) })}</span>
        <span>{t('consult.banner.mrn', { mrn: ltr(patient.mrn) })}</span>
        <span>{t('consult.banner.dob', { date: formatCalendarDate(patient.dateOfBirth) })}</span>
      </div>
      <ul className="flex flex-wrap gap-x-5 gap-y-1 text-sm">
        <li className="inline-flex items-center gap-1.5 font-bold">
          {allergies === null ? <><CircleHelp aria-hidden className="size-4" />{t('consult.banner.allergiesNotRecorded')}</>
            : allergies.length === 0 ? <><CircleCheck aria-hidden className="size-4" />{t('banner.noAllergies')}</>
            : <><CircleAlert aria-hidden className="size-4" />{t('consult.banner.allergies', { list: allergies.map(a => `${a.name} (${t(`patient360.allergyStatus.${allergyStatusOf[a.status]}`)})`).join(', ') })}</>}
        </li>
        <li className="inline-flex items-center gap-1.5">
          {banner.treatmentConsent
            ? <><CircleCheck aria-hidden className="size-4" />{t('consult.banner.consentGiven')}</>
            : <><CircleAlert aria-hidden className="size-4" />{t('consult.banner.consentNotRecorded')}</>}
        </li>
        <li className="inline-flex items-center gap-1.5">
          <DoorOpen aria-hidden className="size-4" />
          {encounter.room
            ? t('consult.banner.when', { date: formatDate(started, timeZone), time: formatTime(started, timeZone), room: encounter.room })
            : t('consult.banner.whenNoRoom', { date: formatDate(started, timeZone), time: formatTime(started, timeZone) })}
        </li>
      </ul>
    </section>
  );
}

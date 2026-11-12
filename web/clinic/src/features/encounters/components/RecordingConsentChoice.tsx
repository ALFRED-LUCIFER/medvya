import { useI18n } from '@medvya/i18n';
import { recordingConsents, type RecordingConsent } from '../domain/note';

type Props = { value: RecordingConsent | null; readOnly: boolean; onChange: (value: RecordingConsent) => void };

/**
 * The doctor records the patient's answer about recording this visit (MDV-P2-044), saved with the note; the note cannot be signed without one.
 * Withdrawn is offered only once the patient had agreed. shortcut: no recording exists before MDV-P3, so every answer keeps the typed note.
 */
export default function RecordingConsentChoice({ value, readOnly, onChange }: Props) {
  const { t } = useI18n();
  const offered = recordingConsents.filter(c => c !== 'Withdrawn' || value === 'Agreed' || value === 'Withdrawn');
  return (
    <fieldset className="flex flex-col gap-2 rounded-lg border p-3" disabled={readOnly}>
      <legend className="px-1 font-bold">{t('recordingConsent.legend')}</legend>
      {value === null && !readOnly && <p className="text-sm text-muted-foreground">{t('recordingConsent.notRecorded')}</p>}
      <div className="flex flex-wrap gap-x-5 gap-y-1">
        {offered.map(c => (
          <label key={c} className="inline-flex min-h-11 items-center gap-2">
            <input type="radio" name="recording-consent" value={c} checked={value === c} onChange={() => onChange(c)} className="size-4 accent-brand-700" />
            {t(`recordingConsent.${c}`)}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

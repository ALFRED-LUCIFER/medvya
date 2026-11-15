import { useI18n } from '@medvya/i18n';
import { Button, Field, Input } from '@medvya/ui';
import { fieldError } from './fieldStyles';

type Props = {
  id: string; hasSignature: boolean;
  /** The signed-in doctor's own image: only they add or replace it; an administrator may remove one. */
  own: boolean;
  problem: 'type' | 'size' | 'failed' | null; busy: boolean;
  onChoose: (file: File | undefined) => void; onRemove: () => void;
};

/** A doctor's signature image for printed prescriptions: whether one is saved, the file field for the doctor's own, and Remove. */
export default function SignatureImage({ id, hasSignature, own, problem, busy, onChoose, onRemove }: Props) {
  const { t } = useI18n();
  return (
    <div className="flex flex-col gap-2">
      <p className="font-bold">{hasSignature ? t('doctors.signature.has') : t('doctors.signature.none')}</p>
      <p className="text-sm text-muted-foreground">{t('doctors.signature.notice')}</p>
      {!own && <p className="text-sm text-muted-foreground">{t('doctors.signature.ownOnly')}</p>}
      {!own && problem && <p role="alert" className={fieldError}>{t(`doctors.signature.error.${problem}`)}</p>}
      <div className="flex flex-wrap items-end gap-3">
        {own && (
          <Field id={`sig-${id}`} label={hasSignature ? t('doctors.signature.replace') : t('doctors.signature.choose')} hint={t('doctors.signature.hint')}
            error={problem ? t(`doctors.signature.error.${problem}`) : undefined} className="min-w-60">
            <Input type="file" accept="image/png,image/jpeg" className="h-auto py-2" disabled={busy}
              onChange={e => { onChoose(e.currentTarget.files?.[0]); e.currentTarget.value = ''; }} />
          </Field>
        )}
        {hasSignature && <Button type="button" variant="outline" loading={busy} onClick={onRemove}>{t('doctors.signature.remove')}</Button>}
      </div>
    </div>
  );
}

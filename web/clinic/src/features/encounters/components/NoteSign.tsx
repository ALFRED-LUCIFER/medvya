import { useRef } from 'react';
import { CircleAlert, CircleCheck, Circle } from 'lucide-react';
import { ltr, useI18n, type MessageKey } from '@medvya/i18n';
import { Button, Dialog, DialogContent, DialogDescription, DialogTitle } from '@medvya/ui';
import { checklistKey, type ChecklistItem } from '../domain/checklist';
import { sectionLabelKey, withTitle, type NoteSignature, type SignFailure, type SignPatient } from '../domain/note';

export type SignProblem = SignFailure | { kind: 'unsaved' } | { kind: 'finding' } | null;
type Props = {
  canSign: boolean; signing: boolean; patient: SignPatient | null; doctorName: string | null; confirming: boolean; problem: SignProblem;
  /** Checklist items still to do (MDV-P2-044): Sign stays locked until none is left. */
  remaining: number;
  onRequest: () => void; onCancel: () => void; onConfirm: () => void;
};

/** E-06: Sign note, with a confirmation that says what signing means. Hidden without the capability (the server decides either way). */
export function SignNote(p: Props) {
  const { t, tCount, formatCalendarDate } = useI18n();
  const signRef = useRef<HTMLButtonElement>(null);
  return (
    <div className="flex flex-col gap-3">
      {p.canSign && (
        <div className="flex flex-wrap items-center gap-3">
          <Button ref={signRef} loading={p.signing} disabled={p.remaining > 0} aria-describedby="sign-remaining" onClick={p.onRequest}>{t('noteSign.button')}</Button>
          <p id="sign-remaining" aria-live="polite" className="inline-flex items-center gap-1.5 font-bold">
            {p.remaining > 0 ? <CircleAlert aria-hidden className="size-4" /> : <CircleCheck aria-hidden className="size-4" />}
            {p.remaining > 0 ? tCount('signChecklist.remaining', p.remaining) : t('signChecklist.ready')}
          </p>
        </div>
      )}
      <SignProblemText problem={p.problem} />
      <Dialog open={p.confirming} onOpenChange={open => { if (!open && !p.signing) p.onCancel(); }}>
        <DialogContent closeLabel={t('noteSign.confirm.close')}
          // The dialog is opened by state, not by a trigger, so give focus back to the Sign button by hand (it is gone once the note is signed).
          // Not when blank sections closed it: the editor has put focus on the first one.
          onCloseAutoFocus={e => { if (p.problem?.kind === 'blank') return; e.preventDefault(); signRef.current?.focus(); }}>
          <DialogTitle>{t('noteSign.confirm.title', { name: p.patient?.name ?? '' })}</DialogTitle>
          {p.patient && <p className="font-bold">{t('noteSign.confirm.patient', { mrn: ltr(p.patient.mrn), dob: formatCalendarDate(p.patient.dateOfBirth) })}</p>}
          <DialogDescription>{t('noteSign.confirm.body')}</DialogDescription>
          <p className="text-sm text-muted-foreground">{t('noteSign.confirm.stepUp')}</p>
          <div className="flex flex-wrap gap-3">
            <Button loading={p.signing} onClick={p.onConfirm}>{t('noteSign.confirm.confirm', { doctor: withTitle(p.doctorName ?? '') })}</Button>
            <Button variant="outline" disabled={p.signing} onClick={p.onCancel}>{t('noteSign.confirm.cancel')}</Button>
          </div>
        </DialogContent>
      </Dialog>
    </div>
  );
}

/** The review-and-sign checklist (MDV-P2-044): each item in words with an icon, never colour alone. Worked out from the note; nothing is stored. */
export function SignChecklist({ items }: { items: ChecklistItem[] }) {
  const { t } = useI18n();
  return (
    <section aria-labelledby="sign-checklist-title" className="flex flex-col gap-2 rounded-lg border p-3">
      <h3 id="sign-checklist-title" className="font-bold">{t('signChecklist.title')}</h3>
      <ul className="flex flex-col gap-1.5 text-sm">
        {items.map(({ id, state }) => (
          <li key={id} className="flex items-start gap-2">
            {state === 'done' ? <CircleCheck aria-hidden className="mt-0.5 size-4 shrink-0 text-success-700" />
              : state === 'warn' ? <CircleAlert aria-hidden className="mt-0.5 size-4 shrink-0 text-warning-700" />
              : <Circle aria-hidden className="mt-0.5 size-4 shrink-0 text-muted-foreground" />}
            <span>{t(checklistKey(id))}<span className="ms-2 font-bold">{t(`signChecklist.state.${state === 'warn' ? `${id}Warn` : state}` as MessageKey)}</span></span>
          </li>
        ))}
      </ul>
    </section>
  );
}

function SignProblemText({ problem }: { problem: SignProblem }) {
  const { t } = useI18n();
  if (!problem || problem.kind === 'stale' || problem.kind === 'signed') return null; // stale shows the editor's conflict alert; signed reloads the note
  const text = problem.kind === 'blank' ? t('noteSign.blank', { sections: problem.sections.map(id => t(sectionLabelKey(id))).join(', ') }) : t(`noteSign.${problem.kind}`);
  return <p role={problem.kind === 'reauth' ? 'status' : 'alert'} className={problem.kind === 'reauth' ? 'font-bold' : 'font-bold text-danger-700'}>{text}</p>;
}

/** Who signed (the server's one name for the signer), with the licence the owner attested, and when. */
export function SignedLine({ signature, timeZone }: { signature: NoteSignature; timeZone: string | undefined }) {
  const { t, formatDate, formatTime } = useI18n();
  const at = new Date(signature.signedAt);
  const when = { date: formatDate(at, timeZone), time: formatTime(at, timeZone), version: String(signature.versionNo) };
  const name = signature.signerName ?? null;
  return (
    <p role="status" className="font-bold">
      {name === null ? t('noteSign.signedByOther', when)
        : signature.signerLicence ? t('noteSign.signedWithLicence', { ...when, name, licence: ltr(signature.signerLicence) })
        : t('noteSign.signedBy', { ...when, name })}
    </p>
  );
}

import { useRef } from 'react';
import { ltr, useI18n } from '@medvya/i18n';
import { Button, Dialog, DialogContent, DialogDescription, DialogTitle } from '@medvya/ui';
import { sectionLabelKey, withTitle, type NoteSignature, type SignFailure, type SignPatient } from '../domain/note';

export type SignProblem = SignFailure | { kind: 'unsaved' } | null;
type Props = {
  canSign: boolean; signing: boolean; patient: SignPatient | null; doctorName: string | null; confirming: boolean; problem: SignProblem;
  onRequest: () => void; onCancel: () => void; onConfirm: () => void;
};

/** E-06: Sign note, with a confirmation that says what signing means. Hidden without the capability (the server decides either way). */
export function SignNote(p: Props) {
  const { t, formatDate } = useI18n();
  const signRef = useRef<HTMLButtonElement>(null);
  return (
    <div className="flex flex-col gap-3">
      {p.canSign && <div><Button ref={signRef} className="min-h-11" loading={p.signing} onClick={p.onRequest}>{t('noteSign.button')}</Button></div>}
      <SignProblemText problem={p.problem} />
      <Dialog open={p.confirming} onOpenChange={open => { if (!open && !p.signing) p.onCancel(); }}>
        <DialogContent closeLabel={t('noteSign.confirm.close')}
          // The dialog is opened by state, not by a trigger, so give focus back to the Sign button by hand (it is gone once the note is signed).
          // Not when blank sections closed it: the editor has put focus on the first one.
          onCloseAutoFocus={e => { if (p.problem?.kind === 'blank') return; e.preventDefault(); signRef.current?.focus(); }}>
          <DialogTitle>{t('noteSign.confirm.title', { name: p.patient?.name ?? '' })}</DialogTitle>
          {p.patient && <p className="font-bold">{t('noteSign.confirm.patient', { mrn: ltr(p.patient.mrn), dob: formatDate(new Date(`${p.patient.dateOfBirth}T12:00:00Z`), 'UTC'), encounter: ltr(p.patient.encounterId) })}</p>}
          <DialogDescription>{t('noteSign.confirm.body')}</DialogDescription>
          <p className="text-sm text-muted-foreground">{t('noteSign.confirm.stepUp')}</p>
          <div className="flex flex-wrap gap-3">
            <Button className="min-h-11" loading={p.signing} onClick={p.onConfirm}>{t('noteSign.confirm.confirm', { doctor: withTitle(p.doctorName ?? '') })}</Button>
            <Button variant="outline" className="min-h-11" disabled={p.signing} onClick={p.onCancel}>{t('noteSign.confirm.cancel')}</Button>
          </div>
        </DialogContent>
      </Dialog>
    </div>
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

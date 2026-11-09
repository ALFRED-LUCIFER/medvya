import { useI18n } from '@medvya/i18n';
import { Button, Dialog, DialogContent, DialogDescription, DialogTitle } from '@medvya/ui';
import { sectionLabelKey, type NoteSignature, type SignFailure } from '../domain/note';

export type SignProblem = SignFailure | { kind: 'unsaved' } | null;
type Props = {
  canSign: boolean; signing: boolean; confirming: boolean; problem: SignProblem;
  onRequest: () => void; onCancel: () => void; onConfirm: () => void;
};

/** E-06: Sign note, with a confirmation that says what signing means. Hidden without the capability (the server decides either way). */
export function SignNote(p: Props) {
  const { t } = useI18n();
  return (
    <div className="flex flex-col gap-3">
      {p.canSign && <div><Button className="min-h-11" loading={p.signing} onClick={p.onRequest}>{t('noteSign.button')}</Button></div>}
      <SignProblemText problem={p.problem} />
      <Dialog open={p.confirming} onOpenChange={open => { if (!open && !p.signing) p.onCancel(); }}>
        <DialogContent closeLabel={t('noteSign.confirm.close')}>
          <DialogTitle>{t('noteSign.confirm.title')}</DialogTitle>
          <DialogDescription>{t('noteSign.confirm.body')}</DialogDescription>
          <div className="flex flex-wrap gap-3">
            <Button className="min-h-11" loading={p.signing} onClick={p.onConfirm}>{t('noteSign.confirm.confirm')}</Button>
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

/** Who signed and when. The server sends the signer's id only: the name is shown when it is the person looking at it. */
export function SignedLine({ signature, userId, userName, timeZone }: { signature: NoteSignature; userId: string; userName: string; timeZone: string | undefined }) {
  const { t, formatDate, formatTime } = useI18n();
  const at = new Date(signature.signedAt);
  return (
    <p role="status" className="font-bold">
      {t(signature.signedBy === userId ? 'noteSign.signedBy' : 'noteSign.signedByOther',
        { name: userName, date: formatDate(at, timeZone), time: formatTime(at, timeZone), version: String(signature.versionNo) })}
    </p>
  );
}

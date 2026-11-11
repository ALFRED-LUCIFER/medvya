import { useEffect, useRef, type Ref } from 'react';
import { Link } from 'react-router';
import type { UseFormRegister } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, Input, Label, Textarea } from '@medvya/ui';
import { maxReasonLength, maxSectionLength, type AddendumFailure, type AddendumForm, type NoteAddendum, type NoteSignature } from '../domain/note';

type Props = {
  signed: boolean; signature: NoteSignature | null; addenda: NoteAddendum[];
  canAdd: boolean; composing: boolean; problem: AddendumFailure | null; submitting: boolean; invalid: boolean;
  register: UseFormRegister<AddendumForm>;
  onCompose: () => void; onCancel: () => void; onSubmit: () => void; onConfirmIdentity: () => void;
  userId: string; userName: string; timeZone: string | undefined; printPath: string;
};

/** E-06: a signed note's version history, the addendum composer (a new signed version, never an edit) and the way to print. Nothing before signing. */
export default function NoteHistory(p: Props) {
  const { t } = useI18n();
  const textRef = useRef<HTMLTextAreaElement | null>(null);
  const addRef = useRef<HTMLButtonElement>(null);
  const lastRef = useRef<HTMLLIElement>(null);
  const was = useRef({ composing: false, count: p.addenda.length });
  // The button that opened the composer, and the composer itself, unmount: put focus where the doctor works next (the field, then the new
  // version, which is read out, or the button after cancel) rather than letting it fall to the page.
  useEffect(() => {
    if (p.composing && !was.current.composing) textRef.current?.focus();
    if (!p.composing && was.current.composing) (p.addenda.length > was.current.count ? lastRef : addRef).current?.focus();
    was.current = { composing: p.composing, count: p.addenda.length };
  }, [p.composing, p.addenda.length]);
  if (!p.signed || !p.signature) return null;
  const text = p.register('text');
  return (
    <section aria-labelledby="note-history" className="flex flex-col gap-4 rounded-xl border bg-card p-4">
      <h2 id="note-history" className="text-lg font-extrabold">{t('noteHistory.title')}</h2>
      <Versions lastRef={lastRef} signature={p.signature} addenda={p.addenda} userId={p.userId} userName={p.userName} timeZone={p.timeZone} />
      <p className="text-sm text-muted-foreground">{t('notePrint.corrections')}</p>
      <div className="flex flex-wrap gap-3">
        {p.canAdd && !p.composing && <Button ref={addRef} className="min-h-11" onClick={p.onCompose}>{t('noteAddendum.add')}</Button>}
        <Link to={p.printPath} className={buttonVariants({ variant: 'outline', className: 'min-h-11' })}>{t('notePrint.open')}</Link>
      </div>
      {p.composing && (
        <form className="flex flex-col gap-3" noValidate onSubmit={e => { e.preventDefault(); p.onSubmit(); }}>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="addendum-text">{t('noteAddendum.text')}</Label>
            <Textarea id="addendum-text" className="min-h-24" maxLength={maxSectionLength} aria-describedby="addendum-hint" aria-required {...text} ref={e => { text.ref(e); textRef.current = e; }} />
            <p id="addendum-hint" className="text-sm text-muted-foreground">{t('noteAddendum.hint')}</p>
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="addendum-reason">{t('noteAddendum.reason')}</Label>
            <Input id="addendum-reason" className="min-h-11" maxLength={maxReasonLength} aria-required {...p.register('reason')} />
          </div>
          {p.invalid && <p role="alert" className="font-bold text-danger-700">{t('noteAddendum.required')}</p>}
          {p.problem && (
            <div role="alert" className="flex flex-wrap items-center gap-3 font-bold text-danger-700">
              <span>{t(`noteAddendum.${p.problem}`)}</span>
              {p.problem === 'reauth' && <Button type="button" variant="outline" className="min-h-11" onClick={p.onConfirmIdentity}>{t('noteAddendum.confirmIdentity')}</Button>}
            </div>
          )}
          <div className="flex flex-wrap gap-3">
            <Button type="submit" className="min-h-11" loading={p.submitting}>{t('noteAddendum.sign')}</Button>
            <Button type="button" variant="outline" className="min-h-11" disabled={p.submitting} onClick={p.onCancel}>{t('noteAddendum.cancel')}</Button>
          </div>
        </form>
      )}
    </section>
  );
}

/** Version 1 and every addendum in order, each with its signer and time; an addendum also shows why it was added and its text. */
export function Versions({ lastRef, signature, addenda, userId, userName, timeZone }: { lastRef?: Ref<HTMLLIElement>; signature: NoteSignature; addenda: NoteAddendum[]; userId: string; userName: string; timeZone: string | undefined }) {
  const { t, formatDate, formatTime } = useI18n();
  const signedLine = (version: number, by: string, at: string) => t(by === userId ? 'noteHistory.signed' : 'noteHistory.signedOther',
    { version: String(version), name: userName, date: formatDate(new Date(at), timeZone), time: formatTime(new Date(at), timeZone) });
  return (
    <ol className="flex flex-col gap-3">
      <li className="flex flex-col gap-0.5">
        <span className="font-bold">{t('noteHistory.original')}</span>
        <span>{signedLine(signature.versionNo, signature.signedBy, signature.signedAt)}</span>
      </li>
      {addenda.map((a, i) => (
        <li key={a.versionNo} ref={i === addenda.length - 1 ? lastRef : undefined} tabIndex={-1} className="focus:outline-none flex flex-col gap-0.5 border-s-4 border-brand-700 ps-3">
          <span className="font-bold">{t('noteHistory.addendum', { amends: String(a.amendsVersionNo) })}</span>
          <span>{signedLine(a.versionNo, a.signedBy, a.signedAt)}</span>
          <span>{t('noteHistory.reason', { reason: a.reason })}</span>
          <p className="whitespace-pre-wrap">{a.text}</p>
        </li>
      ))}
    </ol>
  );
}

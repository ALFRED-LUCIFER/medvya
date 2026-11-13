import type { UseFormRegisterReturn } from 'react-hook-form';
import { useI18n } from '@medvya/i18n';
import type { ChecklistItem } from '../domain/checklist';
import type { NoteSignature, NoteTemplate, RecordingConsent, SavePhase, SignPatient } from '../domain/note';
import NoteAssist, { type Assist } from './NoteAssist';
import { NoteSectionField, SaveState, SignedBanner, TemplatePicker, type SectionSource, type StructuredProps } from './NoteParts';
import { SignChecklist, SignedLine, SignNote, type SignProblem } from './NoteSign';
import RecordingConsentChoice from './RecordingConsentChoice';

export type NoteEditorProps = StructuredProps & {
  /** MDV-P2-042: the note is on the template suggested for this patient and nothing is saved yet. */
  suggested: boolean;
  register: (sectionId: string) => UseFormRegisterReturn;
  /** Null once the note is signed: nothing can be added to it any more. */
  assist: Assist | null;
  loading: boolean; forbidden: boolean; loadFailed: boolean;
  template: NoteTemplate | null; templateKey: string | null; choices: NoteTemplate[];
  readOnly: boolean; phase: SavePhase; reloading: boolean;
  pendingSwitch: { template: NoteTemplate; dropped: string[] } | null;
  onRequestSwitch: (key: string) => void; onCancelSwitch: () => void; onConfirmSwitch: () => void;
  onRetry: () => void; onReapply: () => void;
  canSign: boolean; signing: boolean; confirming: boolean; signProblem: SignProblem; signature: NoteSignature | null;
  onRequestSign: () => void; onCancelSign: () => void; onConfirmSign: () => void;
  signAsName: string | null; timeZone: string | undefined; patient: SignPatient | null;
  consent: RecordingConsent | null; onConsent: (value: RecordingConsent) => void;
  /** Empty without Sign or once signed. */
  checklist: ChecklistItem[]; remaining: number;
  /** MDV-P1-104: the chief complaint came from nurse intake (null: the doctor's own, or blank). */
  complaintSource: SectionSource | null;
};

/** E-04: the consultation note. Sections come from the template; saving is automatic and its state is always shown. Read-only once not a draft. */
export default function NoteEditor(p: NoteEditorProps) {
  const { t } = useI18n();
  if (p.loading) return <p role="status">{t('noteEditor.loading')}</p>;
  if (p.loadFailed || !p.template || !p.templateKey) return <p role="alert" className="font-bold text-danger-700">{t(p.forbidden ? 'noteEditor.forbidden' : 'noteEditor.loadFailed')}</p>;
  return (
    <section aria-labelledby="note-title" className="flex flex-col gap-4 rounded-xl border bg-card p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h2 id="note-title" className="text-lg font-extrabold">{t('noteEditor.title')}</h2>
        <TemplatePicker templateKey={p.templateKey} choices={p.choices} readOnly={p.readOnly} pendingSwitch={p.pendingSwitch} suggested={p.suggested}
          onRequestSwitch={p.onRequestSwitch} onCancelSwitch={p.onCancelSwitch} onConfirmSwitch={p.onConfirmSwitch} />
      </div>
      <RecordingConsentChoice value={p.consent} readOnly={p.readOnly} onChange={p.onConsent} />
      {!p.readOnly && <p className="text-sm text-muted-foreground">{t('noteEditor.manualBanner')}</p>}
      <SignedBanner signed={p.readOnly} versionNo={p.signature?.versionNo ?? 1} />
      {p.signature && <SignedLine signature={p.signature} timeZone={p.timeZone} />}
      {!p.readOnly && <SaveState phase={p.phase} reloading={p.reloading} onRetry={p.onRetry} onReapply={p.onReapply} />}
      {p.assist && <NoteAssist assist={p.assist} template={p.template} timeZone={p.timeZone} />}
      <div className="flex flex-col gap-4">
        {p.template.sections.map(s => <NoteSectionField key={s.id} section={s} register={p.register} readOnly={p.readOnly} answers={p.answers} onAnswers={p.onAnswers} source={s.id === 'chiefComplaint' ? p.complaintSource : null} />)}
      </div>
      {!p.readOnly && (
        <>
          {!p.canSign && <p className="text-sm text-muted-foreground">{t('noteSign.onlyDoctor')}</p>}
          {p.checklist.length > 0 && <SignChecklist items={p.checklist} />}
          <SignNote canSign={p.canSign} remaining={p.remaining} patient={p.patient} doctorName={p.signAsName} signing={p.signing} confirming={p.confirming} problem={p.signProblem}
            onRequest={p.onRequestSign} onCancel={p.onCancelSign} onConfirm={p.onConfirmSign} />
        </>
      )}
    </section>
  );
}

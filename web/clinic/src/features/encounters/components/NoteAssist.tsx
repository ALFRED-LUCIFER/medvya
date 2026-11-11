import { useI18n } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import { phraseKey, sectionLabelKey, snippetSections, type NoteSections, type NoteTemplate } from '../domain/note';

export type Assist = {
  phrases: string[]; target: string; tooLong: boolean; loading: boolean; failed: boolean;
  /** Verified intake readings of this visit, each already worded with its source. Empty: nothing is verified, Objective stays blank. */
  vitals: string[];
  previous: { id: string; signedAt: string; signedByName: string | null; sections: NoteSections }[];
  onInsertPhrase: (key: string) => void; onInsertVitals: () => void; onInsertSnippet: (noteId: string, sectionId: string) => void;
};
type Props = { assist: Assist; template: NoteTemplate; timeZone: string | undefined };

/** E-04: what the doctor may add to the note. Nothing goes in by itself: each button adds text the doctor then reads and edits. */
export default function NoteAssist({ assist: a, template, timeZone }: Props) {
  const { t, formatDate } = useI18n();
  return (
    <section aria-labelledby="note-assist-title" className="flex flex-col gap-4 rounded-lg border p-3">
      <h3 id="note-assist-title" className="font-bold">{t('noteAssist.title')}</h3>
      {a.loading && <p className="text-sm text-muted-foreground">{t('noteAssist.loading')}</p>}
      {a.failed && <p role="alert" className="text-sm font-bold text-danger-700">{t('noteAssist.failed')}</p>}
      {a.tooLong && <p role="alert" className="text-sm font-bold text-danger-700">{t('noteAssist.tooLong')}</p>}
      {!a.loading && !a.failed && (
        <div className="flex flex-col gap-1.5">
          <h4 className="text-sm font-bold">{t('noteAssist.vitals.title')}</h4>
          {a.vitals.length === 0
            ? <p className="text-sm text-muted-foreground">{t('noteAssist.vitals.empty')}</p>
            : (
              <>
                <ul className="list-disc ps-5 text-sm">{a.vitals.map(line => <li key={line}>{line}</li>)}</ul>
                <div><Button variant="outline" className="min-h-11" onClick={a.onInsertVitals}>{t('noteAssist.vitals.insert')}</Button></div>
              </>
            )}
        </div>
      )}
      <p className="text-sm text-muted-foreground">{t('noteAssist.target', { section: t(sectionLabelKey(a.target)) })}</p>
      {a.phrases.length > 0 && (
        <div role="group" aria-label={t('noteAssist.phrases')} className="flex flex-wrap gap-2">
          {a.phrases.map(key => <Button key={key} variant="outline" className="min-h-11" onClick={() => a.onInsertPhrase(key)}>{t(phraseKey(key))}</Button>)}
        </div>
      )}
      {!a.loading && !a.failed && (
        <div className="flex flex-col gap-2">
          <h4 className="text-sm font-bold">{t('noteAssist.previous.title')}</h4>
          {a.previous.length === 0 && <p className="text-sm text-muted-foreground">{t('noteAssist.previous.empty')}</p>}
          {a.previous.map(n => {
            const date = formatDate(new Date(n.signedAt), timeZone);
            return (
              <details key={n.id} className="rounded-md border p-2">
                <summary className="min-h-11 cursor-pointer py-2 text-sm font-bold">
                  {n.signedByName ? t('noteAssist.previous.header', { date, name: n.signedByName }) : t('noteAssist.previous.headerNoName', { date })}
                </summary>
                <ul className="flex flex-col gap-2">
                  {snippetSections(n, template).map(id => (
                    <li key={id} className="flex flex-wrap items-start justify-between gap-2">
                      <div className="min-w-0 flex-1 text-sm">
                        <p className="font-bold">{t(sectionLabelKey(id))}</p>
                        <p className="line-clamp-3 whitespace-pre-wrap">{n.sections[id]}</p>
                      </div>
                      <Button variant="outline" className="min-h-11" aria-label={t('noteAssist.previous.insertLabel', { section: t(sectionLabelKey(id)), date })} onClick={() => a.onInsertSnippet(n.id, id)}>
                        {t('noteAssist.previous.insert')}
                      </Button>
                    </li>
                  ))}
                </ul>
              </details>
            );
          })}
        </div>
      )}
    </section>
  );
}

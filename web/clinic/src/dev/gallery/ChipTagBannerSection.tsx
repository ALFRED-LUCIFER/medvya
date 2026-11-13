import { CircleAlert, CircleCheck, Info, NotebookPen, Sparkles, Tag as TagIcon, TriangleAlert } from 'lucide-react';
import {
  Banner, Button, Chip, FrontDeskTag, frontDeskLooks, ProvenanceTag, provenanceLooks, StatusChip, statusLooks, Tag, type Tone,
} from '@medvya/ui';

const keys = <T extends object>(o: T) => Object.keys(o) as (keyof T)[];
// Sample words per tone (dev page, synthetic). ai never takes a label: it always reads the AI draft words.
const tones: { tone: Exclude<Tone, 'ai'>; label: string; icon: typeof Info }[] = [
  { tone: 'danger', label: 'Critical result', icon: CircleAlert },
  { tone: 'warning', label: 'Not checked', icon: TriangleAlert },
  { tone: 'success', label: 'Signed', icon: CircleCheck },
  { tone: 'info', label: 'Booked', icon: Info },
  { tone: 'brand', label: 'Next', icon: NotebookPen },
  { tone: 'neutral', label: 'From nurse intake', icon: TagIcon },
];

// MDV-P1-083: the seven tones of Chip, Tag and Banner, with and without an icon; then the wrappers that keep today's looks.
export default function ChipTagBannerSection() {
  return (
    <section aria-labelledby="g-chips" className="flex flex-col gap-3">
      <h2 id="g-chips" className="text-heading">Chip, Tag and Banner</h2>
      <h3 className="text-subheading">Chip</h3>
      <div data-testid="chips" className="flex flex-wrap items-center gap-2">
        {tones.map(t => <Chip key={t.tone} tone={t.tone} label={t.label} icon={t.icon} />)}
        <Chip tone="ai" icon={Sparkles} />
        {tones.map(t => <Chip key={t.tone} tone={t.tone} label={t.label} size="sm" />)}
        <Chip tone="ai" size="sm" />
      </div>
      <h3 className="text-subheading">Tag</h3>
      <div data-testid="tags" className="flex flex-wrap items-center gap-2">
        {tones.map(t => <Tag key={t.tone} tone={t.tone} label={t.label} icon={t.icon} />)}
        <Tag tone="ai" icon={Sparkles} />
        {tones.map(t => <Tag key={t.tone} tone={t.tone} label={t.label} size="sm" />)}
        <Tag tone="ai" size="sm" />
      </div>
      <h3 className="text-subheading">Banner</h3>
      <div data-testid="banners" className="flex max-w-form flex-col gap-2">
        {tones.map(t => <Banner key={t.tone} tone={t.tone} title={t.label} icon={t.icon}>Synthetic example text.</Banner>)}
        <Banner tone="ai" icon={Sparkles} action={<Button variant="outline">Review draft</Button>}>Suggested from the consultation audio.</Banner>
        <Banner tone="neutral" title="No icon">A banner may leave the icon out; the title is always there.</Banner>
      </div>
      <h3 className="text-subheading">Wrappers (looks unchanged)</h3>
      <div data-testid="wrappers" className="flex flex-wrap items-center gap-3 bg-background p-2">
        {keys(statusLooks).map(s => <StatusChip key={s} state={s} />)}
        {keys(provenanceLooks).map(s => <ProvenanceTag key={s} source={s} />)}
        {keys(frontDeskLooks).map(s => <FrontDeskTag key={s} kind={s} />)}
        <FrontDeskTag kind="reason" text="Follow-up visit for blood pressure review" />
      </div>
    </section>
  );
}

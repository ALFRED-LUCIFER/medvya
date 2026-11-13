import { useState } from 'react';
import { Button, Label, SaveStatus, StickyActionBar, Textarea, type SaveState } from '@medvya/ui';

const fields = ['Presenting complaint', 'History', 'Examination', 'Assessment', 'Diagnoses', 'Plan', 'Tests', 'Follow-up'];

// MDV-P1-082: a long form with the layout footer last in the flow; the buttons set SaveStatus. Synthetic sample text.
export default function StickyActionBarSection() {
  const [status, setStatus] = useState<SaveState>();
  return (
    <section aria-labelledby="g-footer" className="flex flex-col gap-3">
      <h2 id="g-footer" className="text-heading">Sticky action bar and save status</h2>
      <div className="flex flex-wrap gap-2">
        <Button variant="outline" onClick={() => setStatus({ state: 'saving' })}>Show saving</Button>
        <Button variant="outline" onClick={() => setStatus({ state: 'saved', time: '10:42' })}>Show saved at 10:42</Button>
        <Button variant="outline" onClick={() => setStatus({ state: 'failed' })}>Show not saved</Button>
      </div>
      <div className="flex max-w-form flex-col gap-4">
        {fields.map((f, i) => (
          <div key={f} className="flex flex-col gap-1.5">
            <Label htmlFor={`g-footer-${i}`}>{f}</Label>
            <Textarea id={`g-footer-${i}`} rows={4} defaultValue={i === 0 ? 'Fever for three days' : ''} />
          </div>
        ))}
      </div>
      <StickyActionBar status={<SaveStatus status={status} />}>
        <Button variant="outline">Preview prescription</Button>
        <Button>Review and sign</Button>
      </StickyActionBar>
    </section>
  );
}

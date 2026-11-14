import { useState } from 'react';
import { Input, Popover, PopoverAnchor, PopoverContent } from '@medvya/ui';

const names = ['Meera Iyer', 'Deepak Nair', 'ميرا أيّر'];

// The anchored panel behind the inline patient search: it drops down under its box, stays open while typing, closes on Escape or a click outside. Synthetic names.
export default function PopoverSection() {
  const [text, setText] = useState('');
  const matches = names.filter(n => text && n.toLowerCase().includes(text.toLowerCase()));
  return (
    <section aria-labelledby="g-popover" className="flex flex-col gap-3">
      <h2 id="g-popover" className="text-heading">Popover: anchored results under a box</h2>
      <Popover open={matches.length > 0} onOpenChange={o => { if (!o) setText(''); }}>
        <PopoverAnchor asChild>
          <div className="w-80"><Input aria-label="Sample search" placeholder="Type me or مي" value={text} onChange={e => setText(e.target.value)} /></div>
        </PopoverAnchor>
        <PopoverContent aria-label="Sample matches" className="w-(--radix-popover-trigger-width)" onOpenAutoFocus={e => e.preventDefault()}
          onInteractOutside={e => { if (e.target instanceof HTMLInputElement) e.preventDefault(); }}>
          <ul className="flex flex-col gap-1">{matches.map(n => <li key={n} className="rounded-md border px-3 py-2">{n}</li>)}</ul>
        </PopoverContent>
      </Popover>
    </section>
  );
}

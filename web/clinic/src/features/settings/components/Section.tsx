import type { ReactNode } from 'react';

/** A tab panel's card: heading, one sentence of what it is for, then content. With an id, the heading takes focus when something jumps to it. */
export function Section({ title, intro, id, children }: { title: string; intro?: string; id?: string; children: ReactNode }) {
  return (
    <section aria-label={title} className="flex flex-col gap-4 rounded-xl border bg-card p-6">
      <h2 id={id} tabIndex={id ? -1 : undefined} className="scroll-mt-20 text-lg font-extrabold focus:outline-none focus-visible:outline-2 focus-visible:outline-ring">{title}</h2>
      {intro && <p className="text-muted-foreground">{intro}</p>}
      {children}
    </section>
  );
}

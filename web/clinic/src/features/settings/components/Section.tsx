import type { ReactNode } from 'react';

/** A tab panel's card: heading, one sentence of what it is for, then content. */
export function Section({ title, intro, children }: { title: string; intro?: string; children: ReactNode }) {
  return (
    <section aria-label={title} className="flex flex-col gap-4 rounded-xl border bg-card p-6">
      <h2 className="text-lg font-extrabold">{title}</h2>
      {intro && <p className="text-muted-foreground">{intro}</p>}
      {children}
    </section>
  );
}

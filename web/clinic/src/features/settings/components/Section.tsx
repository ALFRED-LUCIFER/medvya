import type { ReactNode } from 'react';
import { Card } from '@medvya/ui';

/** A tab panel's card: heading, one sentence of what it is for, then content. With an id, the heading takes focus when something jumps to it. */
export function Section({ title, intro, id, actions, children }: { title: string; intro?: ReactNode; id?: string; actions?: ReactNode; children: ReactNode }) {
  return <Card title={title} intro={intro} headingId={id} actions={actions}>{children}</Card>;
}

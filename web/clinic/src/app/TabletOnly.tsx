import type { ReactNode } from 'react';
import OpenOnTablet from '../features/shell/components/OpenOnTablet';
import { usePhone } from '../features/shell/hooks/usePhone';

/** Composition only: a page that is a clinical or admin write shows "Open this on a tablet or computer" under 768 px (MDV-P1-098). */
// `allowed` keeps one tree when a page switches between a phone-friendly and a blocked mode, so switching never remounts it and loses typing.
export default function TabletOnly({ title, patientName, allowed = false, children }: { title: string; patientName?: string | null; allowed?: boolean; children: ReactNode }) {
  return usePhone() && !allowed ? <OpenOnTablet title={title} patientName={patientName} /> : children;
}

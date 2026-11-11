import { useState } from 'react';
import type { ChartSection } from '../domain/chart';
import { rememberedSection, rememberSection } from '../services/chartSection';

/** Which part of the one-page chart shows (MDV-P2-033), remembered per user for the browser session. */
export function useChartSectionController(userId: string) {
  const [section, setSection] = useState<ChartSection>(() => rememberedSection(userId));
  return { section, onSection: (s: ChartSection) => { rememberSection(userId, s); setSection(s); } };
}

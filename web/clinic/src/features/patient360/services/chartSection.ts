import { parseSection, type ChartSection } from '../domain/chart';

// The chart's section button (MDV-P2-033) is kept for this browser session and this user only: a section code, never patient data.
const key = (userId: string) => `medvya.chart.section.${userId}`;

export function rememberedSection(userId: string): ChartSection {
  try {
    return parseSection(sessionStorage.getItem(key(userId)));
  } catch {
    return 'all'; // storage blocked: start with everything
  }
}

export function rememberSection(userId: string, section: ChartSection) {
  try {
    sessionStorage.setItem(key(userId), section);
  } catch {
    // shortcut: with storage blocked the choice lasts only until the page is left; nothing is lost but the setting.
  }
}

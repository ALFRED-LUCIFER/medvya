import type { Page } from '@playwright/test';

/**
 * MDV-P1-092 (WCAG 2.2 SC 2.4.11): presses Tab through the whole page and returns every focused element that is not fully visible: outside
 * the viewport, or with something else (pinned top bar, patient strip, section bar, footer, or a clipping scroll box) over its centre or
 * the middle of any edge. Mid-edges, not corners, so a rounded pill is not misread. Stops when focus comes back to an element seen. Empty is good.
 */
export async function obscuredWhenTabbing(page: Page, max = 300): Promise<string[]> {
  const problems: string[] = [];
  for (let i = 0; i < max; i++) {
    await page.keyboard.press('Tab');
    const seen = await page.evaluate(() => {
      const el = document.activeElement as HTMLElement | null;
      if (!el || el === document.body) return null;
      if (el.dataset.tabSeen) return 'again';
      el.dataset.tabSeen = '1';
      const r = el.getBoundingClientRect(), mx = r.left + r.width / 2, my = r.top + r.height / 2;
      const pts: [number, number][] = [[mx, my], [r.left + 2, my], [r.right - 2, my], [mx, r.top + 2], [mx, r.bottom - 2]];
      const covered = r.top < 0 || r.bottom > innerHeight || r.left < 0 || r.right > innerWidth
        || pts.some(([x, y]) => { const hit = document.elementFromPoint(x, y); return !hit || !(el.contains(hit) || hit.contains(el)); });
      return covered ? `${el.tagName} "${(el.getAttribute('aria-label') ?? el.textContent ?? '').trim().slice(0, 40)}" @${Math.round(r.top + scrollY)}` : 'ok';
    });
    if (seen === 'again') break;
    if (seen && seen !== 'ok') problems.push(seen);
  }
  return problems;
}

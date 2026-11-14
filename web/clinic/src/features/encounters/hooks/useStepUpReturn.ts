import { useCallback, useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router';
import { parseResume, type ResumeAction, type StepUpReturn } from '../domain/stepUp';

/**
 * Back from the step-up (MDV-P2-047): the button of the action that asked scrolls into view and takes focus, then `confirmed` fills the
 * status line. Nothing is pressed for the doctor: the action still waits for their click.
 */
export function useStepUpReturn(action: ResumeAction): StepUpReturn {
  const [params, setParams] = useSearchParams();
  const [asked] = useState(() => parseResume(params.get('resume')) === action);
  const pending = useRef(asked);
  const [confirmed, setConfirmed] = useState(false);
  // The word leaves the address at once, so a reload or a copied link does not say it again.
  useEffect(() => {
    if (asked && params.has('resume')) setParams(p => { p.delete('resume'); return p; }, { replace: true });
  }, [asked, params, setParams]);
  // Once the doctor clicks or types, a button that appears later never takes the focus away from them.
  useEffect(() => {
    if (!asked) return;
    const stop = () => { pending.current = false; };
    document.addEventListener('pointerdown', stop, { capture: true, once: true });
    document.addEventListener('keydown', stop, { capture: true, once: true });
    return () => { document.removeEventListener('pointerdown', stop, { capture: true }); document.removeEventListener('keydown', stop, { capture: true }); };
  }, [asked]);
  // A callback ref: the button appears only once the section's data has loaded.
  const focus = useCallback((el: HTMLButtonElement | null) => {
    if (!el || !pending.current || el.disabled) return;
    pending.current = false;
    el.scrollIntoView({ block: 'center' });
    el.focus({ preventScroll: true });
    setConfirmed(true); // the status line appears after the focus move, so the screen reader says it after the button
    el.addEventListener('click', () => setConfirmed(false), { once: true }); // pressed: the action's own answer takes over
  }, []);
  return { focus, confirmed };
}

/**
 * The button a step-up returns to (MDV-P2-047): one fixed word in the return address (`?resume=authorise`), never patient data.
 * `authorise` is the prescription's action (authorise, or cancel once authorised); `sign` is Sign note.
 */
export const resumeActions = ['authorise', 'sign'] as const;
export type ResumeAction = (typeof resumeActions)[number];

/** What a section gets back: the callback ref for its button and whether to say identity was confirmed. */
export type StepUpReturn = { focus: (el: HTMLButtonElement | null) => void; confirmed: boolean };

/** Only a word from the list counts; anything else in the address is ignored. */
export const parseResume = (value: string | null): ResumeAction | null => resumeActions.find(a => a === value) ?? null;

/** The step-up's return address: this page's path and, when an action asked, its word. */
export const returnPath = (path: string, resume?: ResumeAction) => (resume ? `${path}?resume=${resume}` : path);

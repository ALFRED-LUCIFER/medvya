import { createContext, useContext, useEffect } from 'react';

/** How a screen gives the offline strip its own words (MDV-P2-056); the app holds the note. */
export const OfflineNoteContext = createContext<(note: string | null) => void>(() => {});

/** While the screen is open, the offline strip says `note`; null leaves the words for every screen. */
export function useOfflineNote(note: string | null) {
  const setNote = useContext(OfflineNoteContext);
  useEffect(() => {
    setNote(note);
    return () => setNote(null);
  }, [note, setNote]);
}

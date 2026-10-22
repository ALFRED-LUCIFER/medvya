import { SystemState } from '@medvya/ui';

/**
 * F-05a. Signing in again returns to the current route (startSignIn keeps the path).
 * shortcut: no drafts exist yet, so nothing is restored and no patient is re-confirmed; wire both when the first draft-bearing screen lands (notes, MDV-P2).
 */
export default function SessionExpiredPage({ onSignIn }: { onSignIn: () => void }) {
  return (
    <main className="grid min-h-screen place-items-center p-4">
      <SystemState state="sessionExpired" onAction={onSignIn} />
    </main>
  );
}

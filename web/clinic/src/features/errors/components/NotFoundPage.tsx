import { SystemState } from '@medvya/ui';

export default function NotFoundPage({ onBackToToday }: { onBackToToday: () => void }) {
  return (
    <main className="grid min-h-screen place-items-center p-4">
      <SystemState state="notFound" onAction={onBackToToday} />
    </main>
  );
}

import { SystemState } from '@medvya/ui';

// A banner over the page, not a replacement, so typed input survives (F-05d).
export default function OfflineBanner({ onRetry }: { onRetry: () => void }) {
  return (
    <div className="fixed end-4 bottom-4 z-50">
      <SystemState state="offline" onAction={onRetry} />
    </div>
  );
}

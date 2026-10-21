import { SystemState } from '@medvya/ui';

/** `standalone` when shown outside the app shell, which already provides the main landmark. */
export default function NotFoundPage({ onBackToToday, standalone = false }: { onBackToToday: () => void; standalone?: boolean }) {
  const Tag = standalone ? 'main' : 'div';
  return (
    <Tag className={`grid place-items-center p-4 ${standalone ? 'min-h-screen' : 'min-h-[60vh]'}`}>
      <SystemState state="notFound" onAction={onBackToToday} />
    </Tag>
  );
}

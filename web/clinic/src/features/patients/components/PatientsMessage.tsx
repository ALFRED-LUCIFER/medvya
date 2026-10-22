import { Button } from '@medvya/ui';

/** Loading, refused and failed states of the patient screens: one heading, one sentence, at most one action. */
export default function PatientsMessage({ title, body, busy = false, alert = false, action }: {
  title: string; body?: string; busy?: boolean; alert?: boolean; action?: { label: string; onClick: () => void };
}) {
  return (
    <div role={alert ? 'alert' : 'status'} aria-busy={busy || undefined} className="flex flex-col items-start gap-2 rounded-xl border bg-card p-6">
      <p className="font-bold">{title}</p>
      {body && <p className="text-sm text-muted-foreground">{body}</p>}
      {action && <Button variant="outline" className="mt-1" onClick={action.onClick}>{action.label}</Button>}
    </div>
  );
}

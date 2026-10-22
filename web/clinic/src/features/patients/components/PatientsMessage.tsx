import { Button } from '@medvya/ui';

/**
 * Loading, refused and failed states of the patient screens: one heading, one sentence, at most one action. Not a live region:
 * each screen keeps one mounted role="status" element and changes its text, which screen readers announce reliably.
 */
export default function PatientsMessage({ title, body, action }: { title: string; body?: string; action?: { label: string; onClick: () => void } }) {
  return (
    <div className="flex flex-col items-start gap-2 rounded-xl border bg-card p-6">
      <p className="font-bold">{title}</p>
      {body && <p className="text-sm text-muted-foreground">{body}</p>}
      {action && <Button variant="outline" className="mt-1" onClick={action.onClick}>{action.label}</Button>}
    </div>
  );
}

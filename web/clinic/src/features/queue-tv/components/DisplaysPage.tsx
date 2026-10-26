import type { FormEventHandler } from 'react';
import type { UseFormReturn } from 'react-hook-form';
import { Copy, MonitorPlay } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, Input, Label } from '@medvya/ui';
import type { Display, DisplayInput } from '../domain/queueTv';

type Props = {
  form: UseFormReturn<DisplayInput>;
  displays: Display[];
  loading: boolean;
  forbidden: boolean;
  loadFailed: boolean;
  adding: boolean;
  addFailed: boolean;
  created: { name: string; link: string } | null;
  copied: boolean;
  revokingId: string | null;
  revokeFailed: boolean;
  onSubmit: FormEventHandler<HTMLFormElement>;
  onCopy: () => void;
  onRevoke: (id: string) => void;
};

const fieldError = 'text-sm font-semibold text-danger-700';

/** Queue displays (MDV-P1-031): the facility's waiting-room TVs. Each gets its link once; revoking one ends it at once. */
export default function DisplaysPage(p: Props) {
  const { t, formatDate } = useI18n();
  const { errors } = p.form.formState;
  return (
    <div className="flex max-w-[720px] flex-col gap-4 rounded-xl border bg-card p-6">
      <h1 className="flex items-center gap-2 text-xl font-extrabold"><MonitorPlay aria-hidden className="size-6" />{t('displays.title')}</h1>
      <p className="text-muted-foreground">{t('displays.intro')}</p>
      {p.loading ? <p role="status">{t('displays.loading')}</p>
        : p.loadFailed ? <p role="alert" className={fieldError}>{t(p.forbidden ? 'displays.forbidden' : 'displays.loadFailed')}</p>
        : <>
          <form noValidate onSubmit={p.onSubmit} className="flex flex-wrap items-end gap-3">
            <div className="flex min-w-60 flex-1 flex-col gap-2">
              <Label htmlFor="display-name">{t('displays.name')}</Label>
              <Input id="display-name" autoComplete="off" className="h-11" placeholder={t('displays.namePlaceholder')} aria-invalid={!!errors.name}
                aria-describedby={errors.name ? 'display-name-error' : undefined} {...p.form.register('name')} />
            </div>
            <Button type="submit" className="min-h-11" loading={p.adding}>{t('displays.add')}</Button>
            {errors.name && <p id="display-name-error" className={`${fieldError} basis-full`}>{t('displays.error.name')}</p>}
          </form>
          <div aria-live="polite" className="flex flex-col gap-2">
            {p.addFailed && <p role="alert" className={fieldError}>{t('displays.addFailed')}</p>}
            {p.created && (
              <div className="flex flex-col gap-2 rounded-lg border border-action-600 p-3">
                <p className="font-bold">{t('displays.created', { name: p.created.name })}</p>
                <Label htmlFor="display-link">{t('displays.link')}</Label>
                <div className="flex flex-wrap gap-2">
                  <Input id="display-link" readOnly value={p.created.link} className="h-11 min-w-0 flex-1 font-mono text-xs" onFocus={e => e.currentTarget.select()} />
                  <Button type="button" variant="outline" className="min-h-11" onClick={p.onCopy}><Copy aria-hidden />{t(p.copied ? 'displays.copied' : 'displays.copy')}</Button>
                </div>
                <p className="text-sm text-muted-foreground">{t('displays.onceOnly')}</p>
              </div>
            )}
          </div>
          {p.displays.length === 0 ? <p className="text-muted-foreground">{t('displays.empty')}</p> : (
            <ul className="flex flex-col divide-y rounded-lg border">
              {p.displays.map(d => (
                <li key={d.id} className="flex flex-wrap items-center justify-between gap-3 p-3">
                  <span className="flex flex-col">
                    <span className="font-bold">{d.name}</span>
                    <span className="text-sm text-muted-foreground">
                      {d.revokedAt ? t('displays.revokedOn', { date: formatDate(new Date(d.revokedAt)) }) : t('displays.addedOn', { date: formatDate(new Date(d.createdAt)) })}
                    </span>
                  </span>
                  {!d.revokedAt && (
                    <Button type="button" variant="outline" className="min-h-11" loading={p.revokingId === d.id} onClick={() => p.onRevoke(d.id)}
                      aria-label={t('displays.revokeNamed', { name: d.name })}>{t('displays.revoke')}</Button>
                  )}
                </li>
              ))}
            </ul>
          )}
          {p.revokeFailed && <p role="alert" className={fieldError}>{t('displays.revokeFailed')}</p>}
        </>}
    </div>
  );
}

import type { ReactNode } from 'react';
import { Check, Link2Off, X } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button } from '@medvya/ui';
import { isAllowed, type InvitationPreview } from '../domain/invitation';

type Props = { languageSwitcher: ReactNode; loading: boolean; failed: boolean; invitation: InvitationPreview | null; onJoin: () => void; onRetry: () => void };

/** F-06 Invite acceptance. Allowed and not-allowed capabilities differ by icon and words, never colour alone. */
export default function InvitationPage({ languageSwitcher, loading, failed, invitation, onJoin, onRetry }: Props) {
  const { t, formatDate } = useI18n();
  let body;
  if (loading) body = <p aria-busy="true">{t('invite.loading')}</p>;
  else if (failed)
    body = (
      <div role="alert" className="flex flex-col items-start gap-3">
        <p>{t('invite.error')}</p>
        <Button variant="outline" onClick={onRetry}>{t('invite.retry')}</Button>
      </div>
    );
  else if (!invitation)
    body = (
      <div role="alert" className="flex flex-col items-start gap-2.5">
        <Link2Off className="size-7 text-ink-600" aria-hidden />
        <h1 className="text-xl font-extrabold">{t('invite.expired.title')}</h1>
        <p className="text-sm text-muted-foreground">{t('invite.expired.body')}</p>
      </div>
    );
  else {
    const role = t(`role.${invitation.role}`);
    body = (
      <>
        <h1 className="text-2xl font-extrabold">{t('invite.title', { clinic: invitation.clinic })}</h1>
        <p className="text-muted-foreground">
          {invitation.inviter ? t('invite.invitedBy', { name: invitation.inviter, role }) : t('invite.invited', { role })}
        </p>
        <section aria-labelledby="invite-capabilities" className="flex flex-col gap-2 rounded-lg bg-muted p-3 text-sm">
          <h2 id="invite-capabilities" className="font-bold">{t('invite.capabilities')}</h2>
          <ul className="flex flex-col gap-1.5">
            {invitation.capabilities.map(({ capability, access }) => isAllowed(access) ? (
              <li key={capability} className="flex items-start gap-2">
                <Check className="mt-0.5 size-4 shrink-0 text-success-700" aria-hidden />
                <span>
                  <span className="sr-only">{t('invite.allowed')} </span>
                  {t(`capability.${capability}`)}
                  {access !== 'Full' && <span className="text-muted-foreground">{` · ${t(`access.${access}`)}`}</span>}
                </span>
              </li>
            ) : (
              <li key={capability} className="flex items-start gap-2 text-muted-foreground">
                <X className="mt-0.5 size-4 shrink-0" aria-hidden />
                <span><span className="sr-only">{t('invite.notAllowed')} </span>{t(`capability.${capability}`)}</span>
              </li>
            ))}
          </ul>
          <p className="font-semibold">{t('invite.facility', { facility: invitation.facility })}</p>
        </section>
        {invitation.licenceVerificationRequired && <p className="text-sm">{t('invite.licence')}</p>}
        <Button onClick={onJoin}>{t('invite.cta')}</Button>
        <p className="text-xs text-muted-foreground">{t('invite.expires', { date: formatDate(new Date(invitation.expiresAt)) })}</p>
      </>
    );
  }
  return (
    <main className="grid min-h-screen place-items-center p-4">
      <div className="flex w-full max-w-[440px] flex-col gap-4 rounded-lg border bg-card p-7">{body}{languageSwitcher}</div>
    </main>
  );
}

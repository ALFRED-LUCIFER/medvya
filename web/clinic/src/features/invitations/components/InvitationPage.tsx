import type { ReactNode } from 'react';
import { Check, Link2Off, Mail, X } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Button, PageHeader, PageMessage } from '@medvya/ui';
import AuthFrame from '../../session/components/AuthFrame';
import { isAllowed, type InvitationPreview } from '../domain/invitation';

type Props = { languageSwitcher: ReactNode; loading: boolean; failed: boolean; invitation: InvitationPreview | null; onJoin: () => void; onRetry: () => void };

/** F-06 Invite acceptance in the sign-in frame (6 Nov design). Allowed and not-allowed capabilities differ by icon and words, never colour alone. */
export default function InvitationPage({ languageSwitcher, loading, failed, invitation, onJoin, onRetry }: Props) {
  const { t, formatDate } = useI18n();
  let body;
  if (loading) body = <PageMessage kind="busy">{t('invite.loading')}</PageMessage>;
  else if (failed) body = <PageMessage kind="error" action={<Button variant="outline" onClick={onRetry}>{t('invite.retry')}</Button>}>{t('invite.error')}</PageMessage>;
  else if (!invitation)
    body = (
      <div role="alert" className="flex flex-col items-start gap-2.5">
        <Link2Off className="size-7 text-ink-600" aria-hidden />
        {/* Wraps rather than truncates: nothing loads into this title, and on a phone it must be read whole. */}
        <PageHeader className="[&_h1]:whitespace-normal" title={t('invite.expired.title')} description={t('invite.expired.body')} />
      </div>
    );
  else {
    const role = t(`role.${invitation.role}`);
    body = (
      <>
        <span className="inline-flex items-center gap-1.5 self-start rounded-full bg-brand-50 px-2.5 py-1 text-xs font-bold text-brand-700">
          <Mail className="size-4" aria-hidden />{t('signin.invite.tag')}
        </span>
        {/* Wraps rather than truncates: nothing loads into this title, and on a phone it must be read whole. */}
        <PageHeader className="[&_h1]:whitespace-normal" title={t('invite.title', { clinic: invitation.clinic })}
          description={invitation.inviter ? t('invite.invitedBy', { name: invitation.inviter, role }) : t('invite.invited', { role })} />
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
        <Button className="min-h-12" onClick={onJoin}>{t('invite.cta')}</Button>
        <p className="text-xs text-muted-foreground">{t('invite.expires', { date: formatDate(new Date(invitation.expiresAt)) })}</p>
      </>
    );
  }
  return (
    <AuthFrame languageSwitcher={languageSwitcher}>{body}</AuthFrame>
  );
}

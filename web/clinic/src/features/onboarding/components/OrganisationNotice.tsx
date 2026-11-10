import { Info, Lock, PauseCircle } from 'lucide-react';
import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import type { OrganisationState } from '../../session/domain/session';

/** A plain notice per organisation state (ADR-016), in words and an icon, never colour alone. Approved shows nothing. */
export default function OrganisationNotice({ state, requestPath }: { state: OrganisationState | undefined; requestPath?: string }) {
  const { t } = useI18n();
  if (!state || state === 'Approved') return null;
  const Icon = state === 'Suspended' ? PauseCircle : state === 'Rejected' ? Lock : Info;
  return (
    <p role="note" className="flex items-start gap-2 border-b bg-muted px-4 py-2 text-sm">
      <Icon className="mt-0.5 size-4 shrink-0" aria-hidden />
      <span>{t(`organisation.notice.${state}`)}{requestPath && state !== 'Suspended' && <>{' '}<Link to={requestPath} className="font-semibold underline">{t('organisation.notice.link')}</Link></>}</span>
    </p>
  );
}

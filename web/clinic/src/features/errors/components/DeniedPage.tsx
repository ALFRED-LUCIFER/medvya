import { Link } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { Button, buttonVariants, PageContainer, PageHeader, PageMessage } from '@medvya/ui';
import type { OrganisationState } from '../../session/domain/session';

/**
 * A screen this role may not open (MDV-P1-096). It never says what the screen holds or whose it is; the server's 403 is the control.
 * Before Medvya has approved the organisation (or while it is suspended) the reason is that, not the role (#1365 F-026): the page says so
 * in the shell notice's own words, and an owner gets the way to the request.
 */
export default function DeniedPage({ onHome, organisationState, requestPath }: { onHome: () => void; organisationState?: OrganisationState; requestPath?: string }) {
  const { t } = useI18n();
  if (organisationState && organisationState !== 'Approved')
    return (
      <PageContainer>
        <PageHeader title={t('denied.organisation.title')} />
        <PageMessage kind="denied" action={requestPath && organisationState !== 'Suspended'
          ? <Link to={requestPath} className={buttonVariants()}>{t('denied.organisation.request')}</Link>
          : <Button onClick={onHome}>{t('state.notFound.cta')}</Button>}>{t(`organisation.notice.${organisationState}`)}</PageMessage>
      </PageContainer>
    );
  return (
    <PageContainer>
      <PageHeader title={t('denied.title')} />
      <PageMessage kind="denied" action={<Button onClick={onHome}>{t('state.notFound.cta')}</Button>}>{t('denied.body')}</PageMessage>
    </PageContainer>
  );
}

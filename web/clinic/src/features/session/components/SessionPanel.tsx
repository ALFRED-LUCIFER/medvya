import { useI18n } from '@medvya/i18n';
import { Button } from '@medvya/ui';

type Props = {
  loading: boolean;
  displayName: string | null;
  failed: boolean;
  signingOut: boolean;
  onSignIn: () => void;
  onSignOut: () => void;
};

export default function SessionPanel({ loading, displayName, failed, signingOut, onSignIn, onSignOut }: Props) {
  const { t } = useI18n();
  if (loading) return null;
  if (displayName)
    return (
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p>{t('session.signedInAs', { name: displayName })}</p>
        <Button variant="outline" loading={signingOut} onClick={onSignOut}>{t('session.signOut')}</Button>
      </div>
    );
  return (
    <div className="flex flex-col gap-3">
      {failed && <p role="alert" className="text-sm font-semibold text-danger-700">{t('session.failed')}</p>}
      <Button onClick={onSignIn}>{t('session.signIn')}</Button>
    </div>
  );
}

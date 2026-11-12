import { NavLink } from 'react-router';
import { LayoutGrid, Stethoscope } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { buttonVariants } from '@medvya/ui';

/** The reception board's two views (MDV audit UX-05): visits by status, or by doctor with the waiting room. One screen, one switch. */
export default function FrontDeskViews({ boardPath, doctorsPath }: { boardPath: string; doctorsPath: string }) {
  const { t } = useI18n();
  const link = ({ isActive }: { isActive: boolean }) => buttonVariants({ variant: isActive ? 'selected' : 'ghost', className: 'aria-[current=page]:font-extrabold' });
  return (
    // A group, not a second navigation landmark beside the app's sidebar; the current view is aria-current="page".
    <div role="group" aria-label={t('frontDesk.views')} className="inline-flex gap-1 rounded-lg border bg-card p-1">
      <NavLink end to={boardPath} className={link}><LayoutGrid aria-hidden />{t('frontDesk.view.board')}</NavLink>
      <NavLink end to={doctorsPath} className={link}><Stethoscope aria-hidden />{t('frontDesk.view.doctors')}</NavLink>
    </div>
  );
}

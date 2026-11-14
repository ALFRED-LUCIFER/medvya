import { NavLink } from 'react-router';
import { useI18n } from '@medvya/i18n';
import { buttonVariants } from '@medvya/ui';
import type { RecordTab } from '../domain/recordTabs';

/** The Patient record's tabs (MDV-P2-051): one record, the tabs this role may open. The current one is aria-current="page". */
export default function RecordTabs({ tabs }: { tabs: { tab: RecordTab; path: string }[] }) {
  const { t } = useI18n();
  const link = ({ isActive }: { isActive: boolean }) => buttonVariants({ variant: isActive ? 'selected' : 'ghost', className: 'aria-[current=page]:font-extrabold' });
  return (
    // A group, not a second navigation landmark beside the app's menu (as the Front desk views).
    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 print:hidden">
      <p id="record-tabs" className="text-label text-muted-foreground">{t('record.title')}</p>
      <div role="group" aria-labelledby="record-tabs" className="inline-flex flex-wrap gap-1 rounded-lg bg-muted p-1">
        {tabs.map(({ tab, path }) => <NavLink key={tab} end to={path} className={link}>{t(`record.tab.${tab}`)}</NavLink>)}
      </div>
    </div>
  );
}

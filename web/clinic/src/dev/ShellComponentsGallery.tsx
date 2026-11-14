import { useState } from 'react';
import { ArrowLeft, Bell, CalendarDays, ClipboardList, LayoutDashboard, Settings, Stethoscope, Users } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { AccountMenu, Button, NavRailItem, PageHeader, Tooltip } from '@medvya/ui';

// MDV-P1-084 on /__ui. Labels reuse catalogue keys, including the longest navigation names in each language. Synthetic data only.
const rail = [
  { key: 'nav.frontDesk', icon: LayoutDashboard }, { key: 'nav.today', icon: CalendarDays }, { key: 'nav.patients', icon: Users },
  { key: 'nav.schedule', icon: CalendarDays }, { key: 'nav.nurse', icon: Stethoscope }, { key: 'nav.inbox', icon: ClipboardList },
  { key: 'nav.settings', icon: Settings },
] as const;

export default function ShellComponentsGallery() {
  const { t } = useI18n();
  const [date, setDate] = useState<string>();
  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-lg font-bold">Shell components (AccountMenu, NavRailItem, PageHeader, Tooltip)</h2>
      <div className="flex flex-wrap items-start gap-6">
        <nav aria-label="Rail sample" className="flex w-rail flex-col gap-1 rounded-lg bg-brand-950 p-1">
          {rail.map((r, i) => <NavRailItem key={r.key} href="#rail" icon={r.icon} label={t(r.key)} aria-current={i === 0 ? 'page' : undefined} />)}
        </nav>
        <div className="flex min-w-0 flex-1 flex-col gap-6">
          <div className="flex items-center gap-3">
            <AccountMenu name="Meera Iyer" roleLabel={t('role.SoloDoctor')} onLocaleChange={() => {}} onMyDevices={() => {}} onChangeFacility={() => {}} onSignOut={() => {}} />
            <Tooltip content={t('shell.notifications')}>
              <Button data-testid="tip" variant="ghost" size="icon" aria-label={t('shell.notifications')}><Bell className="size-5" aria-hidden /></Button>
            </Tooltip>
          </div>
          <div data-testid="header-sample" className="rounded-lg border bg-card p-4">
            <PageHeader
              title={t('nav.frontDesk')}
              back={<Button variant="ghost" size="icon" aria-label={t('onboarding.back')}><ArrowLeft className="rtl:rotate-180" aria-hidden /></Button>}
              meta={date}
              actions={<><Button variant="outline" onClick={() => setDate('Fri 13 Nov 2026')}>Load the date</Button><Button>{t('nav.register')}</Button></>}
            />
          </div>
        </div>
      </div>
    </section>
  );
}

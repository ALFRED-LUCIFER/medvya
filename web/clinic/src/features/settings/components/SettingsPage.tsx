import type { ReactNode } from 'react';
import { Settings } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@medvya/ui';
import { settingsTabs, type SettingsTab } from '../domain/settings';

type Props = { tab: SettingsTab; onTab: (tab: SettingsTab) => void; panels: Record<SettingsTab, ReactNode> };

/**
 * Practice settings (F-08, MDV-P1-005). Notifications and Data arrive with MDV-P1-006, Doctors and Staff with MDV-P1-007/008.
 * Panels mount only while their tab is open, so a tab loads its data when it is first shown.
 */
export default function SettingsPage({ tab, onTab, panels }: Props) {
  const { t } = useI18n();
  return (
    <div className="flex max-w-[960px] flex-col gap-4">
      <h1 className="flex items-center gap-2 text-xl font-extrabold"><Settings aria-hidden className="size-6" />{t('settings.title')}</h1>
      <Tabs value={tab} onValueChange={v => onTab(v as SettingsTab)}>
        <TabsList aria-label={t('settings.tabs')} className="h-auto w-full justify-start gap-1 rounded-xl border bg-card p-1">
          {settingsTabs.map(id => <TabsTrigger key={id} value={id} className="min-h-11 flex-none px-4 text-base font-bold text-foreground underline-offset-8 data-[state=active]:underline">{t(`settings.tab.${id}`)}</TabsTrigger>)}
        </TabsList>
        {settingsTabs.map(id => <TabsContent key={id} value={id} className="flex flex-col gap-4">{panels[id]}</TabsContent>)}
      </Tabs>
    </div>
  );
}

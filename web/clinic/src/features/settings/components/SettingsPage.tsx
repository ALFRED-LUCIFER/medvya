import type { ReactNode } from 'react';
import { Settings } from 'lucide-react';
import { useI18n } from '@medvya/i18n';
import { Tabs, TabsContent, TabsList, TabsTrigger, PageContainer, PageHeader } from '@medvya/ui';
import { groupOf, settingsGroupIds, settingsGroups, type SettingsTab } from '../domain/settings';

type Props = { tab: SettingsTab; onTab: (tab: SettingsTab) => void; panels: Record<SettingsTab, ReactNode> };

/**
 * Practice settings (F-08, MDV-P1-005). Services arrive in P4.
 * Panels mount only while their tab is open, so a tab loads its data when it is first shown.
 */
export default function SettingsPage({ tab, onTab, panels }: Props) {
  const { t } = useI18n();
  return (
    <PageContainer width="reading">
      <PageHeader title={<span className="flex items-center gap-2"><Settings aria-hidden className="size-6" />{t('settings.title')}</span>} />
      {/* Groups first, then the group's own tabs (MDV audit UX-04): one row each, so nothing wraps and every subject has its own tab. */}
      <Tabs value={groupOf(tab)} onValueChange={g => onTab(settingsGroups[g as keyof typeof settingsGroups][0])}>
        <TabsList aria-label={t('settings.groups')} className="h-auto w-full flex-wrap justify-start gap-1 rounded-xl border bg-card p-1">
          {settingsGroupIds.map(g => <TabsTrigger key={g} value={g} className="min-h-11 flex-none px-4 text-base font-bold text-foreground underline-offset-8 data-[state=active]:underline">{t(`settings.group.${g}`)}</TabsTrigger>)}
        </TabsList>
        {settingsGroupIds.map(g => (
          <TabsContent key={g} value={g} className="flex flex-col gap-4">
            <Tabs value={tab} onValueChange={v => onTab(v as SettingsTab)} className="flex flex-col gap-4">
              {settingsGroups[g].length > 1 && (
                <TabsList aria-label={t('settings.tabs')} className="h-auto w-full flex-wrap justify-start gap-1 bg-transparent p-0">
                  {settingsGroups[g].map(id => <TabsTrigger key={id} value={id} className="min-h-11 flex-none rounded-md border px-3 font-bold text-foreground data-[state=active]:border-2 data-[state=active]:border-primary data-[state=active]:bg-brand-50">{t(`settings.tab.${id}`)}</TabsTrigger>)}
                </TabsList>
              )}
              {(settingsGroups[g] as readonly SettingsTab[]).map(id => <TabsContent key={id} value={id} className="flex flex-col gap-4">{panels[id]}</TabsContent>)}
            </Tabs>
          </TabsContent>
        ))}
      </Tabs>
    </PageContainer>
  );
}

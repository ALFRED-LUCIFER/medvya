import { useI18n } from '@medvya/i18n';
import { Icon, type IconName } from '@medvya/ui-native';
import { theme } from '@medvya/tokens';
import { Tabs } from 'expo-router';
import type { ColorValue } from 'react-native';
import { useInboxController } from '../../features/inbox/hooks/useInboxController';
import { useSignedIn } from '../../features/session/hooks/session';
import { tabsFor, type TabKey } from '../../features/session/domain/session';

const icons: Record<TabKey, IconName> = { today: 'space-dashboard', patients: 'groups', consult: 'mic', inbox: 'inbox' };

/** Role-based tabs: a tab the role lacks is not registered at all (Tabs.Protected), not merely hidden. */
export default function TabsLayout() {
  const { t } = useI18n();
  const { session } = useSignedIn();
  const allowed = tabsFor(session.staff.role);
  const { count } = useInboxController();
  const tab = (key: TabKey) => ({
    title: t(`mobile.staff.tab.${key}`),
    tabBarIcon: ({ color }: { color: ColorValue }) => <Icon name={icons[key]} color={typeof color === 'string' ? color : undefined} />,
  });
  return (
    <Tabs screenOptions={{ headerShown: false, tabBarActiveTintColor: theme.color.brand700, tabBarInactiveTintColor: theme.color.ink600, tabBarLabelStyle: { fontWeight: '600' } }}>
      <Tabs.Screen name="index" options={tab('today')} />
      <Tabs.Screen name="patients" options={tab('patients')} />
      <Tabs.Protected guard={allowed.includes('consult')}>
        <Tabs.Screen name="consult" options={tab('consult')} />
      </Tabs.Protected>
      <Tabs.Screen name="inbox" options={{ ...tab('inbox'), tabBarBadge: count || undefined }} />
    </Tabs>
  );
}

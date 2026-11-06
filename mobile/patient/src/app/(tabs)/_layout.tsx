import { useI18n, type MessageKey } from '@medvya/i18n';
import { Icon, type IconName } from '@medvya/ui-native';
import { theme } from '@medvya/tokens';
import { Tabs } from 'expo-router';
import type { ColorValue } from 'react-native';

const tabs: { name: string; key: MessageKey; icon: IconName }[] = [
  { name: 'index', key: 'mobile.patient.tab.home', icon: 'home' },
  { name: 'book', key: 'mobile.patient.tab.book', icon: 'calendar-month' },
  { name: 'records', key: 'mobile.patient.tab.records', icon: 'folder-shared' },
  { name: 'messages', key: 'mobile.patient.tab.messages', icon: 'chat-bubble-outline' },
  { name: 'profile', key: 'mobile.patient.tab.profile', icon: 'person-outline' },
];

export default function TabsLayout() {
  const { t } = useI18n();
  return (
    <Tabs screenOptions={{ headerShown: false, tabBarActiveTintColor: theme.color.patient700, tabBarInactiveTintColor: theme.color.ink600, tabBarLabelStyle: { fontWeight: '600', fontSize: 12 } }}>
      {tabs.map(tab => (
        <Tabs.Screen key={tab.name} name={tab.name} options={{
          title: t(tab.key),
          tabBarIcon: ({ color }: { color: ColorValue }) => <Icon name={tab.icon} color={typeof color === 'string' ? color : undefined} />,
        }} />
      ))}
    </Tabs>
  );
}

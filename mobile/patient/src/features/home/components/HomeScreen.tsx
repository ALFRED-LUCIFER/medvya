import { useI18n, type MessageKey } from '@medvya/i18n';
import { AppHeader, Button, Card, Icon, Loading, Screen, Section, StateMessage, Text, type IconName } from '@medvya/ui-native';
import { theme } from '@medvya/tokens';
import { Pressable, StyleSheet, View } from 'react-native';
import type { Session } from '../../session/domain/session';
import type { Home, Task } from '../domain/home';

type Props = {
  session: Session; home: Home | null; loading: boolean; error: string | null; refreshing: boolean; onRefresh: () => void;
  emergency: string; onCallEmergency: () => void; onCallClinic: () => void; onDirections: () => void;
};

const taskIcon: Record<Task['kind'], IconName> = { pay: 'payments', video: 'videocam', lab: 'science', medicine: 'medication' };

export function HomeScreen(vm: Props) {
  const { t, formatDate, formatTime, formatMoney } = useI18n();
  const tz = vm.session.clinic.timeZone;
  const firstName = vm.session.person.name.split(' ')[0] ?? vm.session.person.name;
  const header = <AppHeader title={t('mobile.home.hello', { name: firstName })} subtitle={`${vm.session.clinic.name} · ${vm.session.clinic.branch}`}
    initials={vm.session.person.initials} action={{ icon: 'call', label: t('mobile.home.callClinic'), onPress: vm.onCallClinic }} />;

  if (vm.loading) return <Screen header={header} scroll={false}><Loading label={t('mobile.common.loading')} /></Screen>;
  if (vm.error || !vm.home) return <Screen header={header} scroll={false}><StateMessage icon="cloud-off" title={t('mobile.common.unavailable')} body={t((vm.error ?? 'mobile.error.generic') as MessageKey)} /></Screen>;

  const when = (iso: string) => `${formatDate(new Date(iso), tz)} · ${formatTime(new Date(iso), tz)}`;
  const task = (k: Task): { title: string; line: string; action?: string } => {
    switch (k.kind) {
      case 'pay': return { title: t('mobile.home.pay', { amount: formatMoney(k.amountMinor, k.currency) }), line: t('mobile.home.payLine', { invoice: k.invoice }), action: t('mobile.home.payNow') };
      case 'video': return { title: t('mobile.home.video', { when: when(k.at) }), line: t('mobile.home.videoLine', { doctor: k.doctor }), action: t('mobile.home.joinVideo') };
      case 'lab': return { title: t('mobile.home.lab', { test: k.test, where: k.where }), line: t('mobile.home.labLine') };
      case 'medicine': return { title: t('mobile.home.medicine', { name: k.name }), line: k.dose };
    }
  };
  const next = vm.home.nextVisit;
  return (
    <Screen header={header} refresh={{ refreshing: vm.refreshing, onRefresh: vm.onRefresh }}>
      {next ? (
        <View style={styles.hero}>
          <Text variant="overline" tone={theme.color.patient50}>{t('mobile.home.nextVisit')}</Text>
          <Text variant="title" tone={theme.color.card}>{when(next.at)}</Text>
          <Text tone={theme.color.patient50}>{t('mobile.home.nextLine', { doctor: next.doctor, reason: next.reason })}</Text>
          <View style={styles.heroActions}>
            <View style={styles.flex}><Button label={t('mobile.home.directions')} variant="inverse" icon="directions" onPress={vm.onDirections} /></View>
            <View style={styles.flex}><Button label={t('mobile.home.reschedule')} variant="inverse" disabled hint={t('mobile.common.comingSoon')} /></View>
          </View>
        </View>
      ) : <StateMessage icon="event-available" title={t('mobile.home.noVisit')} />}

      <Section title={t('mobile.home.forYou')}>
        {vm.home.tasks.map(k => {
          const v = task(k);
          return (
            <Card key={k.id}>
              <View style={styles.taskHead}>
                <Icon name={taskIcon[k.kind]} color={theme.color.patient700} />
                <View style={styles.flex}>
                  <Text variant="label">{v.title}</Text>
                  <Text variant="caption">{v.line}</Text>
                </View>
              </View>
              {v.action ? <Button label={v.action} disabled hint={t('mobile.common.comingSoon')} /> : null}
            </Card>
          );
        })}
      </Section>

      <Pressable onPress={vm.onCallEmergency} accessibilityRole="button" accessibilityLabel={t('mobile.home.emergency', { number: vm.emergency })}
        style={({ pressed }) => [styles.emergency, pressed && styles.pressed]}>
        <Icon name="emergency" color={theme.color.danger700} />
        <Text variant="label" tone={theme.color.danger700} style={styles.flex}>{t('mobile.home.emergency', { number: vm.emergency })}</Text>
        <Icon name="call" color={theme.color.danger700} />
      </Pressable>
    </Screen>
  );
}

const styles = StyleSheet.create({
  hero: { backgroundColor: theme.color.patient700, borderRadius: 20, padding: 20, gap: 8 },
  heroActions: { flexDirection: 'row', gap: 10, marginTop: 8 },
  flex: { flex: 1 },
  taskHead: { flexDirection: 'row', alignItems: 'flex-start', gap: 12 },
  emergency: { flexDirection: 'row', alignItems: 'center', gap: 10, padding: 16, minHeight: 56, borderRadius: 16, borderWidth: 1, borderColor: theme.color.danger700, backgroundColor: theme.color.danger50 },
  pressed: { opacity: 0.8 },
});

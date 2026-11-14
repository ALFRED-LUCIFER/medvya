import { useI18n, type MessageKey } from '@medvya/i18n';
import { AppHeader, Avatar, Button, Card, Chip, ListRow, Loading, Screen, Section, Stat, StateMessage, Text, type IconName, type Tone } from '@medvya/ui-native';
import { theme } from '@medvya/tokens';
import { statuses, type Status } from '@medvya/tokens/status';
import { StyleSheet, View } from 'react-native';
import type { Session } from '../../session/domain/session';
import type { Flag, QueueStatus, Today } from '../domain/today';

const flagLook: Record<Flag['kind'], { icon: IconName; tone: Tone }> = {
  allergy: { icon: statuses.allergy.native, tone: statuses.allergy.tone },
  result: { icon: 'trending-up', tone: 'warning' },
  brief: { icon: statuses.ai.native, tone: statuses.ai.tone },
};
// Queue states take the words, tone and glyph of the visit state they are (MDV-P5-057).
const queueLook = (s: QueueStatus) => statuses[({ withDoctor: 'InConsult', next: 'ReadyForDoctor', waiting: 'CheckedIn', booked: 'Booked' } as const satisfies Record<QueueStatus, Status>)[s]];

type Props = {
  session: Session; today: Today | null; loading: boolean; error: string | null; refreshing: boolean; onRefresh: () => void; canConsult: boolean;
  onOpenAccount: () => void; onOpenPatient: (id: string) => void; onStartConsult: () => void };

export function TodayScreen(vm: Props) {
  const { t } = useI18n();
  const header = <AppHeader title={t('mobile.staff.tab.today')} subtitle={`${vm.session.facility.name} · ${vm.session.staff.name}`}
    initials={t('mobile.monogram')} action={{ icon: 'account-circle', label: t('mobile.account.title'), onPress: vm.onOpenAccount }} />;

  if (vm.loading) return <Screen header={header} scroll={false}><Loading label={t('mobile.common.loading')} /></Screen>;
  if (vm.error || !vm.today) return <Screen header={header} scroll={false}><StateMessage icon="cloud-off" title={t('mobile.common.unavailable')} body={t((vm.error ?? 'mobile.error.generic') as MessageKey)} /></Screen>;

  const { next, queue } = vm.today;
  return (
    <Screen header={header} refresh={{ refreshing: vm.refreshing, onRefresh: vm.onRefresh }}>
      {next ? (
        <Card accent={theme.color.brand700}>
          <Text variant="overline">{t('mobile.today.upNext')}</Text>
          <View style={styles.who}>
            <Avatar initials={next.initials} size={48} />
            <View style={styles.whoText}>
              <Text variant="title">{next.name}</Text>
              <Text variant="caption">{t('mobile.today.nextLine', { age: next.age, sex: t(`mobile.sex.${next.sex}`), reason: next.reason, room: next.room })}</Text>
            </View>
          </View>
          <View style={styles.flags}>
            {next.flags.map(f => <Chip key={f.label} label={f.kind === 'allergy' ? t(statuses.allergy.label, { substance: f.label }) : f.label} tone={flagLook[f.kind].tone} icon={flagLook[f.kind].icon} />)}
          </View>
          {vm.canConsult
            ? <Button label={t('mobile.today.startConsult')} icon="mic" onPress={vm.onStartConsult} />
            : <Button label={t('mobile.today.openRecord')} variant="secondary" onPress={() => vm.onOpenPatient(next.patientId)} />}
        </Card>
      ) : <StateMessage icon="event-available" title={t('mobile.today.nobodyNext')} />}

      <View style={styles.stats}>
        <Stat label={t('mobile.today.seen')} value={String(vm.today.seen)} />
        <Stat label={t('mobile.today.waiting')} value={String(vm.today.waiting)} />
        <Stat label={t('mobile.today.toReview')} value={String(vm.today.toReview)} />
      </View>

      <Section title={t('mobile.today.queue')} aside={t('mobile.today.queueCount', { n: queue.length })}>
        <Card style={styles.list}>
          {queue.map((q, i) => (
            <View key={q.token} style={[styles.rowWrap, i > 0 && styles.divider]}>
              <ListRow
                title={q.name}
                subtitle={t('mobile.today.queueLine', { age: q.age, sex: t(`mobile.sex.${q.sex}`), note: q.note })}
                leading={<Text variant="caption" style={styles.token}>{q.token}</Text>}
                trailing={<View style={styles.trailing}>
                  <Chip label={t(queueLook(q.status).label)} tone={queueLook(q.status).tone} icon={queueLook(q.status).native} />
                  {q.viaApp ? <Chip label={t('mobile.today.viaApp')} tone="brand" icon="smartphone" /> : null}
                </View>}
                onPress={() => vm.onOpenPatient(q.patientId)}
              />
            </View>
          ))}
        </Card>
      </Section>
    </Screen>
  );
}

const styles = StyleSheet.create({
  who: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  whoText: { flex: 1, gap: 2 },
  flags: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  stats: { flexDirection: 'row', gap: 10 },
  list: { paddingVertical: 4, gap: 0 },
  rowWrap: { paddingVertical: 2 },
  divider: { borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: theme.color.line },
  token: { width: 44, fontVariant: ['tabular-nums'], fontWeight: '700', color: theme.color.brand700, writingDirection: 'ltr' },
  trailing: { alignItems: 'flex-end', gap: 4 },
});

import { ltr, useI18n, type MessageKey } from '@medvya/i18n';
import { AppHeader, Avatar, Banner, Button, Card, Chip, ListRow, Loading, Screen, Section, Stat, StateMessage, Text, type IconName, type Tone } from '@medvya/ui-native';
import { theme } from '@medvya/tokens';
import { statuses, type Status } from '@medvya/tokens/status';
import { StyleSheet, View } from 'react-native';
import type { Session } from '../../session/domain/session';
import type { AllergyState, Flag, QueueStatus, Today } from '../domain/today';

const flagLook: Record<Exclude<Flag['kind'], 'allergies'>, { icon: IconName; tone: Tone }> = {
  allergy: { icon: statuses.allergy.native, tone: statuses.allergy.tone },
  result: { icon: 'trending-up', tone: 'warning' },
  brief: { icon: statuses.ai.native, tone: statuses.ai.tone },
};
// The server's allergy state as the web's Today words it: an icon and words, never colour alone; "not recorded" never reads as "none".
const allergyLook: Record<AllergyState, { icon: IconName; tone: Tone; label: MessageKey }> = {
  Listed: { icon: statuses.allergy.native, tone: statuses.allergy.tone, label: 'today.allergy.listed' },
  NoKnownAllergies: { icon: 'check-circle', tone: 'neutral', label: 'banner.noAllergies' },
  NotRecorded: { icon: 'help-outline', tone: 'warning', label: 'banner.allergiesNotRecorded' },
};
// Queue states take the words, tone and glyph of the visit state they are (MDV-P5-057).
const queueLook = (s: QueueStatus) => statuses[({ withDoctor: 'InConsult', next: 'ReadyForDoctor', waiting: 'CheckedIn', booked: 'Booked' } as const satisfies Record<QueueStatus, Status>)[s]];

type Props = {
  session: Session; today: Today | null; loading: boolean; error: string | null; refreshing: boolean; onRefresh: () => void; canConsult: boolean;
  offlineSince: string | null; onOpenAccount: () => void; onOpenPatient: (id: string) => void; onStartConsult: () => void };

export function TodayScreen(vm: Props) {
  const { t } = useI18n();
  const header = <AppHeader title={t('mobile.staff.tab.today')} subtitle={`${vm.session.facility.name} · ${vm.session.staff.name}`}
    initials={t('mobile.monogram')} action={{ icon: 'account-circle', label: t('mobile.account.title'), onPress: vm.onOpenAccount }} />;

  if (vm.loading) return <Screen header={header} scroll={false}><Loading label={t('mobile.common.loading')} /></Screen>;
  if (vm.error || !vm.today) return <Screen header={header} scroll={false}><StateMessage icon="cloud-off" title={t('mobile.common.unavailable')} body={t((vm.error ?? 'mobile.error.generic') as MessageKey)} /></Screen>;

  const { next, queue } = vm.today;
  const sex = (s: Today['queue'][number]['sex']) => (s ? t(`mobile.sex.${s}`) : '');
  const nextLine = next && t('mobile.today.nextLine', { age: next.age ?? '', sex: sex(next.sex), reason: next.reason, room: next.room });
  const flags = next?.flags.map(f => (f.kind === 'allergies' ? { ...allergyLook[f.state], label: t(allergyLook[f.state].label) }
    : { ...flagLook[f.kind], label: f.kind === 'allergy' ? t(statuses.allergy.label, { substance: f.label }) : f.label })) ?? [];
  return (
    <Screen header={header} refresh={{ refreshing: vm.refreshing, onRefresh: vm.onRefresh }}>
      {vm.offlineSince ? <Banner tone="warning" icon="cloud-off" text={t('mobile.today.offline', { time: ltr(vm.offlineSince) })} /> : null}
      {next ? (
        <Card accent={theme.color.brand700}>
          {/* One stop for the screen reader: who, why, where and every flag, instead of each chip on its own. */}
          <View style={styles.summary} accessible accessibilityLabel={[t('mobile.today.upNext'), next.name, nextLine, ...flags.map(f => f.label)].join(', ')}>
            <Text variant="overline">{t('mobile.today.upNext')}</Text>
            <View style={styles.who}>
              <Avatar initials={next.initials} size={48} />
              <View style={styles.whoText}>
                <Text variant="title">{next.name}</Text>
                <Text variant="caption">{nextLine}</Text>
              </View>
            </View>
            <View style={styles.flags}>
              {flags.map(f => <Chip key={f.label} {...f} />)}
            </View>
          </View>
          {vm.canConsult
            ? <Button label={t('mobile.today.startConsult')} icon="mic" onPress={vm.onStartConsult} />
            : <Button label={t('mobile.today.openRecord')} variant="secondary" onPress={() => vm.onOpenPatient(next.patientId)} />}
        </Card>
      ) : <StateMessage icon="event-available" title={t('mobile.today.nobodyNext')} />}

      <View style={styles.stats}>
        <Stat label={t('mobile.today.seen')} value={String(vm.today.seen)} />
        <Stat label={t('mobile.today.waiting')} value={String(vm.today.waiting)} />
        <Stat label={t('mobile.today.toReview')} value={vm.today.toReview === null ? '' : String(vm.today.toReview)} />
      </View>

      <Section title={t('mobile.today.queue')} aside={t('mobile.today.queueCount', { n: queue.length })}>
        <Card style={styles.list}>
          {queue.map((q, i) => (
            <View key={`${q.patientId}:${q.at}`} style={[styles.rowWrap, i > 0 && styles.divider]}>
              <ListRow
                title={q.name}
                subtitle={t('mobile.today.queueLine', { age: q.age ?? '', sex: sex(q.sex), note: q.note })}
                leading={<Text variant="caption" style={styles.token}>{ltr(q.token)}</Text>}
                chips={[
                  { label: t(queueLook(q.status).label), tone: queueLook(q.status).tone, icon: queueLook(q.status).native },
                  ...(q.viaApp ? [{ label: t('mobile.today.viaApp'), tone: 'brand' as const, icon: 'smartphone' as const }] : []),
                ]}
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
  summary: { gap: 10 },
  who: { flexDirection: 'row', alignItems: 'center', gap: 12 },
  whoText: { flex: 1, gap: 2 },
  flags: { flexDirection: 'row', flexWrap: 'wrap', gap: 6 },
  stats: { flexDirection: 'row', gap: 10 },
  list: { paddingVertical: 4, gap: 0 },
  rowWrap: { paddingVertical: 2 },
  divider: { borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: theme.color.line },
  token: { width: 44, fontVariant: ['tabular-nums'], fontWeight: '700', color: theme.color.brand700 },
});

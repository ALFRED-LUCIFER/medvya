import { useI18n, type MessageKey } from '@medvya/i18n';
import { AppHeader, Card, Chip, ListRow, Loading, Screen, Segmented, StateMessage, Text, type IconName, type Tone } from '@medvya/ui-native';
import { theme } from '@medvya/tokens';
import { StyleSheet, View } from 'react-native';
import type { Session } from '../../session/domain/session';
import type { Records, RecordsTab, Result } from '../domain/records';

type Props = { session: Session; tab: RecordsTab; onTab: (t: RecordsTab) => void; records: Records | null; loading: boolean; error: string | null };

const flagLook: Record<Result['flag'], { tone: Tone; icon: IconName }> = {
  high: { tone: 'warning', icon: 'arrow-upward' },
  low: { tone: 'warning', icon: 'arrow-downward' },
  normal: { tone: 'success', icon: 'check' },
};

/** Only signed summaries and reviewed results reach this screen, in plain words. */
export function RecordsScreen(vm: Props) {
  const { t, formatDate } = useI18n();
  const day = (d: string) => formatDate(new Date(`${d}T12:00:00Z`), 'UTC');
  const header = <AppHeader title={t('mobile.patient.tab.records')} subtitle={`${vm.session.clinic.name} · ${vm.session.clinic.branch}`} initials={vm.session.person.initials} />;
  return (
    <Screen header={header}>
      <Segmented value={vm.tab} onChange={vm.onTab} options={[
        { key: 'visits', label: t('mobile.records.visits') },
        { key: 'results', label: t('mobile.welcome.results') },
        { key: 'medicines', label: t('mobile.welcome.medicines') },
      ]} />
      {vm.loading ? <Loading label={t('mobile.common.loading')} />
        : vm.error || !vm.records ? <StateMessage icon="cloud-off" title={t('mobile.common.unavailable')} body={t((vm.error ?? 'mobile.error.generic') as MessageKey)} />
        : vm.tab === 'visits' ? vm.records.visits.map(v => (
            <Card key={v.id}>
              <Text variant="heading">{v.title}</Text>
              <Text variant="caption">{day(v.date)}</Text>
              <Text>{v.summary}</Text>
              <Chip label={t('mobile.records.signedBy', { doctor: v.signedBy })} tone="success" icon="verified" />
            </Card>
          ))
        : vm.tab === 'results' ? vm.records.results.map(r => (
            <Card key={r.id}>
              <View style={styles.line}>
                <Text variant="heading" style={styles.flex}>{r.test}</Text>
                <Text variant="heading">{r.value}</Text>
              </View>
              <View style={styles.line}>
                <Chip label={t(`mobile.records.flag.${r.flag}`)} tone={flagLook[r.flag].tone} icon={flagLook[r.flag].icon} />
                <Text variant="caption">{day(r.date)}</Text>
              </View>
              <Text>{t('mobile.records.doctorSays', { note: r.doctorNote })}</Text>
            </Card>
          ))
        : (
          <Card style={styles.list}>
            {vm.records.medicines.map((m, i) => (
              <View key={m.id} style={i > 0 && styles.divider}>
                <ListRow title={m.name} subtitle={m.dose} trailing={m.changed ? <Chip label={t('mobile.records.changed')} tone="info" icon="update" /> : undefined} />
              </View>
            ))}
          </Card>
        )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  line: { flexDirection: 'row', alignItems: 'center', gap: 10, justifyContent: 'space-between' },
  flex: { flex: 1 },
  list: { paddingVertical: 4, gap: 0 },
  divider: { borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: theme.color.line },
});

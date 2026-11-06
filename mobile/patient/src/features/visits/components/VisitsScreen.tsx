import { useI18n, type MessageKey } from '@medvya/i18n';
import { AppHeader, Button, Card, Chip, Icon, ListRow, Loading, Screen, Section, StateMessage, Text } from '@medvya/ui-native';
import { theme } from '@medvya/tokens';
import { StyleSheet, View } from 'react-native';
import type { Session } from '../../session/domain/session';
import type { Visit } from '../domain/visits';

type Props = { session: Session; visits: { upcoming: Visit[]; past: Visit[] } | null; loading: boolean; error: string | null };

// Booking, cancelling and rescheduling go through the same booking service as reception (MDV-P5-036); until then the button is disabled.
export function VisitsScreen(vm: Props) {
  const { t, formatDate, formatTime } = useI18n();
  const tz = vm.session.clinic.timeZone;
  const header = <AppHeader title={t('mobile.visits.title')} subtitle={`${vm.session.clinic.name} · ${vm.session.clinic.branch}`} initials={vm.session.person.initials} />;
  return (
    <Screen header={header} footer={<Button label={t('mobile.visits.book')} icon="add" disabled hint={t('mobile.common.comingSoon')} />}>
      {vm.loading ? <Loading label={t('mobile.common.loading')} />
        : vm.error || !vm.visits ? <StateMessage icon="cloud-off" title={t('mobile.common.unavailable')} body={t((vm.error ?? 'mobile.error.generic') as MessageKey)} />
        : <>
          <Section title={t('mobile.visits.upcoming')}>
            {vm.visits.upcoming.length === 0 ? <StateMessage icon="event-available" title={t('mobile.visits.noneUpcoming')} /> : vm.visits.upcoming.map(v => (
              <Card key={v.id} accent={theme.color.patient700}>
                <View style={styles.line}>
                  <Icon name="event" color={theme.color.patient700} />
                  <Text variant="heading" style={styles.flex}>{`${formatDate(new Date(v.at), tz)} · ${formatTime(new Date(v.at), tz)}`}</Text>
                </View>
                <Text>{t('mobile.visits.line', { doctor: v.doctor, reason: v.reason })}</Text>
                <Chip label={t('mobile.visits.booked')} tone="success" icon="event-available" />
              </Card>
            ))}
          </Section>
          <Section title={t('mobile.visits.past')}>
            <Card style={styles.list}>
              {vm.visits.past.map((v, i) => (
                <View key={v.id} style={i > 0 && styles.divider}>
                  <ListRow title={v.reason} subtitle={`${formatDate(new Date(v.at), tz)} · ${v.doctor}`}
                    trailing={<Chip label={t(`mobile.visits.status.${v.status}`)} tone={v.status === 'cancelled' ? 'neutral' : 'brand'} />} />
                </View>
              ))}
            </Card>
          </Section>
        </>}
    </Screen>
  );
}

const styles = StyleSheet.create({
  line: { flexDirection: 'row', alignItems: 'center', gap: 10 },
  flex: { flex: 1 },
  list: { paddingVertical: 4, gap: 0 },
  divider: { borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: theme.color.line },
});

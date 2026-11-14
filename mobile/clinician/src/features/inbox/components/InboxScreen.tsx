import { useI18n, type MessageKey } from '@medvya/i18n';
import { AppHeader, Avatar, Banner, Button, Card, Chip, Icon, Loading, Screen, Section, StateMessage, Text } from '@medvya/ui-native';
import { theme } from '@medvya/tokens';
import { StyleSheet, View } from 'react-native';
import type { Session } from '../../session/domain/session';
import type { Inbox } from '../domain/inbox';

type Props = { session: Session; inbox: Inbox | null; clinical: boolean; loading: boolean; error: string | null; onOpenAccount: () => void };

// Acting on results and replying arrive with their tickets; until then the buttons are visible but disabled with a reason.
export function InboxScreen(vm: Props) {
  const { t } = useI18n();
  const header = <AppHeader title={t('mobile.staff.tab.inbox')} subtitle={`${vm.session.facility.name} · ${vm.session.staff.name}`}
    initials={t('mobile.monogram')} action={{ icon: 'account-circle', label: t('mobile.account.title'), onPress: vm.onOpenAccount }} />;
  if (vm.loading) return <Screen header={header} scroll={false}><Loading label={t('mobile.common.loading')} /></Screen>;
  if (vm.error || !vm.inbox) return <Screen header={header} scroll={false}><StateMessage icon="cloud-off" title={t('mobile.common.unavailable')} body={t((vm.error ?? 'mobile.error.generic') as MessageKey)} /></Screen>;
  const { results, messages } = vm.inbox;
  return (
    <Screen header={header}>
      {vm.clinical ? (
        <Section title={t('mobile.inbox.results')}>
          {results.length === 0 ? <Banner tone="success" icon="task-alt" text={t('mobile.inbox.noResults')} /> : results.map(r => (
            <Card key={r.id}>
              <View style={styles.line}>
                <Icon name={r.severity === 'critical' ? 'error' : 'warning-amber'} color={r.severity === 'critical' ? theme.color.danger700 : theme.color.warning700} />
                <Text variant="label" style={styles.flex}>{t('mobile.inbox.resultLine', { patient: r.patient, test: r.test, value: r.value })}</Text>
              </View>
              <Chip label={t(r.severity === 'critical' ? 'mobile.inbox.critical' : 'mobile.inbox.abnormal')} tone={r.severity === 'critical' ? 'danger' : 'warning'} />
              <Button label={t('mobile.inbox.markReviewed')} accessibilityLabel={t('mobile.inbox.markReviewedFor', { patient: r.patient, test: r.test })} variant="secondary" disabled hint={t('mobile.common.comingSoon')} />
            </Card>
          ))}
        </Section>
      ) : null}
      <Section title={t('mobile.inbox.messages')}>
        {messages.length === 0 ? <Banner tone="success" icon="task-alt" text={t('mobile.inbox.noMessages')} /> : messages.map(m => (
          <Card key={m.id}>
            <View style={styles.line}>
              <Avatar initials={m.initials} size={36} />
              <Text variant="label" style={styles.flex}>{m.patient}</Text>
              <Chip label={t('mobile.inbox.replyBy', { time: m.replyBy })} tone="warning" icon="schedule" />
            </View>
            <Text>{m.text}</Text>
            <Button label={t('mobile.inbox.reply')} variant="secondary" icon="reply" disabled hint={t('mobile.common.comingSoon')} />
          </Card>
        ))}
      </Section>
    </Screen>
  );
}

const styles = StyleSheet.create({
  line: { flexDirection: 'row', alignItems: 'center', gap: 10 },
  flex: { flex: 1 },
});

import { useI18n } from '@medvya/i18n';
import { AppHeader, Banner, Button, Card, Emblem, Screen, Text } from '@medvya/ui-native';
import { StyleSheet, View } from 'react-native';

type Props = { facility: string; staff: string; onOpenAccount: () => void };

/** The consent step before any recording (Handoff copy). Recording itself is MDV-P5-035, so both answers are disabled for now. */
export function ConsultScreen({ facility, staff, onOpenAccount }: Props) {
  const { t } = useI18n();
  return (
    <Screen header={<AppHeader title={t('mobile.staff.tab.consult')} subtitle={`${facility} · ${staff}`} initials={t('mobile.monogram')}
      action={{ icon: 'account-circle', label: t('mobile.account.title'), onPress: onOpenAccount }} />}
      footer={<>
        <Button label={t('mobile.consult.agrees')} icon="task-alt" disabled hint={t('mobile.common.comingSoon')} />
        <Button label={t('mobile.consult.declines')} variant="secondary" icon="do-not-disturb-on" disabled hint={t('mobile.common.comingSoon')} />
      </>}>
      <View style={styles.top}>
        <Emblem icon="mic" />
        <Text variant="title" accessibilityRole="header" style={styles.center}>{t('mobile.consult.title')}</Text>
      </View>
      <Card>
        <Text variant="overline">{t('mobile.consult.sayThis')}</Text>
        <Text variant="heading">{t('consent.script')}</Text>
      </Card>
      <Banner icon="schedule" text={t('mobile.consult.soon')} />
    </Screen>
  );
}

const styles = StyleSheet.create({
  top: { alignItems: 'center', gap: 12, paddingTop: 8 },
  center: { textAlign: 'center' },
});

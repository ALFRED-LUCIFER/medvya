import { locales, useI18n, type Locale } from '@medvya/i18n';
import { Avatar, Button, Card, Chip, Icon, IconButton, ListRow, Screen, Section, Text } from '@medvya/ui-native';
import { theme } from '@medvya/tokens';
import { I18nManager, StyleSheet, View } from 'react-native';
import type { Facility, Staff } from '../../session/domain/session';

type Props = { staff: Staff; facility: Facility; biometric: boolean; unlockMethod: string; locale: Locale; version: string; onLock: () => void; onSignOut: () => void; onBack: () => void };

export function AccountScreen(vm: Props) {
  const { t } = useI18n();
  const row = (i: number) => (i > 0 ? styles.divider : undefined);
  return (
    <Screen header={
      <View style={styles.bar}>
        <IconButton icon={I18nManager.isRTL ? 'arrow-forward' : 'arrow-back'} label={t('mobile.common.back')} onPress={vm.onBack} />
        <Text variant="heading" accessibilityRole="header">{t('mobile.account.title')}</Text>
      </View>
    } footer={<Button label={t('mobile.account.signOut')} variant="secondary" icon="logout" onPress={vm.onSignOut} />}>
      <View style={styles.identity}>
        <Avatar initials={vm.staff.initials} size={64} />
        <Text variant="title">{vm.staff.name}</Text>
        <Text variant="caption">{vm.staff.email}</Text>
        <Chip label={t(`mobile.role.${vm.staff.role}`)} tone="brand" icon="badge" />
      </View>

      <Section title={t('mobile.account.workplace')}>
        <Card style={styles.list}>
          <ListRow title={vm.facility.name} subtitle={t('mobile.signIn.facilityLine', { city: vm.facility.city, opens: vm.facility.opens, closes: vm.facility.closes })}
            leading={<Icon name="storefront" color={theme.color.brand700} />} />
        </Card>
      </Section>

      <Section title={t('mobile.account.security')}>
        <Card style={styles.list}>
          <View style={row(0)}><ListRow title={t('mobile.account.unlock')} subtitle={t(vm.biometric ? 'mobile.account.biometricOn' : 'mobile.account.biometricOff', { method: vm.unlockMethod })}
            leading={<Icon name="face" />} /></View>
          <View style={row(1)}><ListRow title={t('mobile.account.autoLock')} subtitle={t('mobile.account.autoLockLine')} leading={<Icon name="timer" />} /></View>
          <View style={row(2)}><ListRow title={t('mobile.account.lockNow')} leading={<Icon name="lock-outline" />} onPress={vm.onLock} /></View>
        </Card>
      </Section>

      <Section title={t('mobile.account.preferences')}>
        <Card style={styles.list}>
          {/* shortcut: language follows the phone's setting; an in-app choice (with the RTL restart prompt) comes with MDV-P5-034. */}
          <ListRow title={t('app.language')} subtitle={locales.find(l => l.code === vm.locale)?.label} leading={<Icon name="translate" />} />
        </Card>
      </Section>

      <Text variant="caption" style={styles.version}>{t('mobile.account.version', { version: vm.version })}</Text>
    </Screen>
  );
}

const styles = StyleSheet.create({
  bar: { flexDirection: 'row', alignItems: 'center', gap: 12, paddingHorizontal: 16, paddingVertical: 8, backgroundColor: theme.color.card, borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: theme.color.line },
  identity: { alignItems: 'center', gap: 6, paddingVertical: 8 },
  list: { paddingVertical: 4, gap: 0 },
  divider: { borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: theme.color.line },
  version: { textAlign: 'center' },
});

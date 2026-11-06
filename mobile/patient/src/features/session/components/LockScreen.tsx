import { useI18n } from '@medvya/i18n';
import { Avatar, Banner, Button, Screen, Text } from '@medvya/ui-native';
import { StyleSheet, View } from 'react-native';
// shortcut: duplicated in mobile/clinician; both move to packages/auth-client with native tokens (MDV-P5-033).
type Props = { name: string; initials: string; biometric: boolean; failed: boolean; busy: boolean; onUnlock: () => void; onSignInAgain: () => void };

/** Shown after 15 minutes away. Nothing from a record is visible here. */
export function LockScreen(vm: Props) {
  const { t } = useI18n();
  return (
    <Screen scroll={false} footer={<>
      {vm.biometric ? <Button label={t('mobile.lock.unlock')} icon="face" onPress={vm.onUnlock} busy={vm.busy} /> : null}
      <Button label={t('mobile.lock.signInAgain')} variant={vm.biometric ? 'ghost' : 'primary'} onPress={vm.onSignInAgain} />
    </>}>
      <View style={styles.centered}>
        <Avatar initials={vm.initials} size={72} />
        <Text variant="title" accessibilityRole="header">{t('mobile.lock.title')}</Text>
        <Text style={styles.center}>{t('mobile.lock.body', { name: vm.name })}</Text>
        {vm.failed ? <Banner tone="warning" icon="lock-outline" text={t('mobile.lock.failed')} /> : null}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  centered: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: 12 },
  center: { textAlign: 'center' },
});

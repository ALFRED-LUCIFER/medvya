import { useI18n, type MessageKey } from '@medvya/i18n';
import { Avatar, Banner, Button, Screen, Text, TextField } from '@medvya/ui-native';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { StyleSheet, View } from 'react-native';
import type { PinForm } from '../domain/session';

type Props = {
  name: string; initials: string; biometric: boolean; unlockMethod: string; biometricFailed: boolean; showPin: boolean;
  pinForm: UseFormReturn<PinForm>; triesLeft: number | null; error: string | null; busy: boolean;
  onUnlock: () => void; onSubmitPin: () => void; onSwitchUser: () => void;
};

/** Shown after 15 minutes away. Nothing from a record is visible here. */
export function LockScreen(vm: Props) {
  const { t } = useI18n();
  return (
    <Screen footer={<>
      {vm.showPin
        ? <Button label={t('mobile.lock.unlock')} onPress={vm.onSubmitPin} busy={vm.busy} />
        : <Button label={t('mobile.lock.unlock')} icon="face" onPress={vm.onUnlock} busy={vm.busy} />}
      <Button label={t('mobile.lock.switchUser')} variant="ghost" icon="switch-account" onPress={vm.onSwitchUser} />
    </>}>
      <View style={styles.centered}>
        <Avatar initials={vm.initials} size={72} />
        <Text variant="title" accessibilityRole="header">{t('mobile.lock.title')}</Text>
        <Text style={styles.center}>{t('mobile.lock.body', { name: vm.name })}</Text>
      </View>
      {vm.biometric && vm.biometricFailed ? <Banner tone="warning" icon="lock-outline" text={t('mobile.lock.usePinInstead', { method: vm.unlockMethod })} /> : null}
      {vm.triesLeft !== null ? <Banner tone="danger" icon="error-outline" text={t('mobile.pin.wrong', { n: vm.triesLeft })} /> : null}
      {vm.error ? <Banner tone="danger" icon="error-outline" text={t(vm.error as MessageKey)} /> : null}
      {vm.showPin ? (
        <Controller control={vm.pinForm.control} name="pin" render={({ field, fieldState }) => (
          <TextField label={t('mobile.pin.label')} value={field.value} onChangeText={v => field.onChange(v.replace(/\D/g, ''))} onBlur={field.onBlur}
            error={fieldState.error?.message ? t(fieldState.error.message as MessageKey) : undefined}
            keyboardType="number-pad" secureTextEntry autoComplete="off" textContentType="none" maxLength={6} autoFocus
            style={styles.pin} onSubmitEditing={vm.onSubmitPin} />
        )} />
      ) : null}
      {vm.biometric && vm.biometricFailed
        ? <Button label={t('mobile.lock.tryAgain', { method: vm.unlockMethod })} variant="secondary" icon="face" onPress={vm.onUnlock} />
        : null}
    </Screen>
  );
}

const styles = StyleSheet.create({
  centered: { flexGrow: 1, alignItems: 'center', justifyContent: 'center', gap: 12 },
  center: { textAlign: 'center' },
  pin: { letterSpacing: 8, fontVariant: ['tabular-nums'], writingDirection: 'ltr' },
});

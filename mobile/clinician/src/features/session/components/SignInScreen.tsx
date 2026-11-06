import { ltr, useI18n, type MessageKey } from '@medvya/i18n';
import { Avatar, Banner, Button, Emblem, Icon, ListRow, Loading, Screen, Text, TextField } from '@medvya/ui-native';
import { theme } from '@medvya/tokens';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { StyleSheet, View } from 'react-native';
import type { CodeForm, EmailForm, Facility, SignInStep } from '../domain/session';

type Props = {
  step: SignInStep; email: string; busy: boolean; error: string | null;
  emailForm: UseFormReturn<EmailForm>; codeForm: UseFormReturn<CodeForm>;
  onSubmitEmail: () => void; onSubmitCode: () => void; onBack: () => void;
  facilities: Facility[]; facilitiesLoading: boolean; onChooseFacility: (f: Facility) => void;
  biometricAvailable: boolean; onEnableBiometric: () => void; onSkipBiometric: () => void;
};

function Brandmark() {
  const { t } = useI18n();
  return (
    <View style={styles.brand}>
      <Avatar initials={t('mobile.monogram')} solid />
      <View>
        <Text variant="heading">{t('mobile.clinician.name')}</Text>
        <Text variant="caption">{t('mobile.clinician.audience')}</Text>
      </View>
    </View>
  );
}

export function SignInScreen(vm: Props) {
  const { t } = useI18n();
  const error = vm.error ? <Banner tone="danger" icon="error-outline" text={t(vm.error as MessageKey)} /> : null;

  if (vm.step === 'email') return (
    <Screen footer={<Button label={t('mobile.signIn.continue')} onPress={vm.onSubmitEmail} busy={vm.busy} />}>
      <Brandmark />
      <View style={styles.intro}>
        <Text variant="display" accessibilityRole="header">{t('mobile.signIn.title')}</Text>
        <Text>{t('mobile.signIn.emailIntro')}</Text>
      </View>
      <Controller control={vm.emailForm.control} name="email" render={({ field, fieldState }) => (
        <TextField label={t('mobile.signIn.email')} value={field.value} onChangeText={field.onChange} onBlur={field.onBlur}
          error={fieldState.error?.message ? t(fieldState.error.message as MessageKey) : undefined}
          keyboardType="email-address" autoCapitalize="none" autoComplete="email" textContentType="username" autoFocus
          returnKeyType="next" onSubmitEditing={vm.onSubmitEmail} />
      )} />
      {error}
      <Text variant="caption" style={styles.legal}>{t('mobile.signIn.legal')}</Text>
    </Screen>
  );

  if (vm.step === 'code') return (
    <Screen footer={<>
      <Button label={t('mobile.signIn.verify')} onPress={vm.onSubmitCode} busy={vm.busy} />
      <Button label={t('mobile.signIn.otherEmail')} variant="ghost" onPress={vm.onBack} />
    </>}>
      <Brandmark />
      <View style={styles.intro}>
        <Text variant="display" accessibilityRole="header">{t('mobile.signIn.codeTitle')}</Text>
        <Text>{t('mobile.signIn.codeIntro', { email: ltr(vm.email) })}</Text>
      </View>
      <Controller control={vm.codeForm.control} name="code" render={({ field, fieldState }) => (
        <TextField label={t('mobile.signIn.code')} value={field.value} onChangeText={v => field.onChange(v.replace(/\D/g, ''))} onBlur={field.onBlur}
          error={fieldState.error?.message ? t(fieldState.error.message as MessageKey) : undefined}
          keyboardType="number-pad" textContentType="oneTimeCode" autoComplete="one-time-code" maxLength={6} autoFocus
          style={styles.code} onSubmitEditing={vm.onSubmitCode} />
      )} />
      {error}
    </Screen>
  );

  if (vm.step === 'facility') return (
    <Screen>
      <Brandmark />
      <Text variant="display" accessibilityRole="header">{t('mobile.signIn.facilityTitle')}</Text>
      {vm.facilitiesLoading ? <Loading label={t('mobile.common.loading')} /> : vm.facilities.map(f => (
        <View key={f.id} style={styles.option}>
          <ListRow title={f.name} subtitle={t('mobile.signIn.facilityLine', { city: f.city, opens: f.opens, closes: f.closes })}
            leading={<Icon name="storefront" color={theme.color.brand700} />} onPress={() => vm.onChooseFacility(f)} />
        </View>
      ))}
      {error}
    </Screen>
  );

  return (
    <Screen footer={<>
      {vm.biometricAvailable ? <Button label={t('mobile.signIn.biometricOn')} onPress={vm.onEnableBiometric} /> : null}
      <Button label={vm.biometricAvailable ? t('mobile.signIn.notNow') : t('mobile.signIn.continue')} variant={vm.biometricAvailable ? 'ghost' : 'primary'} onPress={vm.onSkipBiometric} />
    </>}>
      <View style={styles.centered}>
        <Emblem icon="face" size={88} />
        <Text variant="title" accessibilityRole="header" style={styles.center}>{t('mobile.signIn.biometricTitle')}</Text>
        <Text style={styles.center}>{t(vm.biometricAvailable ? 'mobile.signIn.biometricBody' : 'mobile.signIn.biometricNone')}</Text>
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  brand: { flexDirection: 'row', alignItems: 'center', gap: 12, marginBottom: 12 },
  intro: { gap: 8 },
  legal: { marginTop: 'auto' },
  code: { letterSpacing: 8, fontVariant: ['tabular-nums'], writingDirection: 'ltr' },
  option: { backgroundColor: theme.color.card, borderRadius: 16, borderWidth: 1, borderColor: theme.color.line, paddingHorizontal: 14 },
  centered: { flexGrow: 1, alignItems: 'center', justifyContent: 'center', gap: 14, paddingHorizontal: 12 },
  center: { textAlign: 'center' },
});

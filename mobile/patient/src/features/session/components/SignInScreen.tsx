import { ltr, useI18n, type MessageKey } from '@medvya/i18n';
import { Avatar, Banner, Button, Chip, Emblem, ListRow, Screen, Segmented, Text, TextField, type IconName } from '@medvya/ui-native';
import { theme } from '@medvya/tokens';
import { Controller, type UseFormReturn } from 'react-hook-form';
import { StyleSheet, View } from 'react-native';
import { dialCode, type CodeForm, type Person, type PhoneForm, type SignInStep } from '../domain/session';

type Props = {
  step: SignInStep; page: number; onPage: (p: number) => void; onStart: () => void;
  phoneForm: UseFormReturn<PhoneForm>; codeForm: UseFormReturn<CodeForm>; maskedPhone: string;
  onSubmitPhone: () => void; onSubmitCode: () => void; onResend: () => void; onBack: () => void; busy: boolean;
  people: Person[]; onChoosePerson: (p: Person) => void;
  biometricAvailable: boolean; onEnableBiometric: () => void; onSkipBiometric: () => void; error: string | null;
};

const pages: { icon: IconName; title: MessageKey; body: MessageKey }[] = [
  { icon: 'favorite-border', title: 'mobile.welcome.title1', body: 'mobile.welcome.body1' },
  { icon: 'verified-user', title: 'mobile.welcome.title2', body: 'mobile.welcome.body2' },
  { icon: 'family-restroom', title: 'mobile.welcome.title3', body: 'mobile.welcome.body3' },
];

/** Three onboarding pages, one at a time (Next and Skip), so it works the same on iOS, Android and in RTL. */
function Welcome({ page, onPage, onStart }: Pick<Props, 'page' | 'onPage' | 'onStart'>) {
  const { t } = useI18n();
  const last = page === pages.length - 1;
  const p = pages[page] ?? pages[0]!;
  return (
    <Screen footer={<>
      <View style={styles.dots} accessible accessibilityLabel={t('mobile.welcome.pageOf', { n: page + 1, total: pages.length })}>
        {pages.map((d, i) => <View key={d.title} style={[styles.dot, i === page && styles.dotOn]} />)}
      </View>
      <Button label={last ? t('mobile.welcome.start') : t('mobile.welcome.next')} icon={last ? 'smartphone' : undefined}
        onPress={last ? onStart : () => onPage(page + 1)} />
      {last ? null : <Button label={t('mobile.welcome.skip')} variant="ghost" onPress={onStart} />}
    </>}>
      <View style={styles.page}>
        <View style={styles.mark}>{page === 0 ? <Avatar initials={t('mobile.monogram')} solid size={72} /> : <Emblem icon={p.icon} size={72} />}</View>
        <Text variant="display" accessibilityRole="header">{t(p.title)}</Text>
        <Text>{t(p.body)}</Text>
        {page === 0 ? (
          <View style={styles.chips}>
            <Chip label={t('mobile.patient.tab.book')} icon="event" tone="brand" />
            <Chip label={t('mobile.welcome.results')} icon="science" tone="brand" />
            <Chip label={t('mobile.welcome.medicines')} icon="medication" tone="brand" />
            <Chip label={t('mobile.patient.tab.messages')} icon="chat-bubble-outline" tone="brand" />
          </View>
        ) : null}
      </View>
    </Screen>
  );
}

export function SignInScreen(vm: Props) {
  const { t } = useI18n();
  const error = vm.error ? <Banner tone="danger" icon="error-outline" text={t(vm.error as MessageKey)} /> : null;

  if (vm.step === 'welcome') return <Welcome page={vm.page} onPage={vm.onPage} onStart={vm.onStart} />;

  if (vm.step === 'phone') return (
    <Screen footer={<Button label={t('mobile.patientSignIn.sendCode')} onPress={vm.onSubmitPhone} busy={vm.busy} />}>
      <Text variant="display" accessibilityRole="header">{t('mobile.patientSignIn.phoneTitle')}</Text>
      <Text>{t('mobile.patientSignIn.phoneIntro')}</Text>
      <Controller control={vm.phoneForm.control} name="country" render={({ field }) => (
        <Segmented value={field.value} onChange={field.onChange} options={[
          { key: 'IN', label: t('mobile.patientSignIn.india', { code: ltr(dialCode('IN')) }) },
          { key: 'AE', label: t('mobile.patientSignIn.uae', { code: ltr(dialCode('AE')) }) },
        ]} />
      )} />
      <Controller control={vm.phoneForm.control} name="number" render={({ field, fieldState }) => (
        <TextField label={t('mobile.patientSignIn.phone')} value={field.value} onChangeText={field.onChange} onBlur={field.onBlur}
          error={fieldState.error?.message ? t(fieldState.error.message as MessageKey) : undefined}
          keyboardType="phone-pad" textContentType="telephoneNumber" autoComplete="tel" autoFocus style={styles.ltr}
          onSubmitEditing={vm.onSubmitPhone} />
      )} />
      {error}
      <Text variant="caption" style={styles.legal}>{t('mobile.patientSignIn.legal')}</Text>
    </Screen>
  );

  if (vm.step === 'code') return (
    <Screen footer={<>
      <Button label={t('mobile.patientSignIn.verify')} onPress={vm.onSubmitCode} busy={vm.busy} />
      <Button label={t('mobile.patientSignIn.changeNumber')} variant="ghost" onPress={vm.onBack} />
    </>}>
      <Text variant="display" accessibilityRole="header">{t('mobile.patientSignIn.codeTitle')}</Text>
      <Text>{t('mobile.patientSignIn.codeIntro', { phone: ltr(vm.maskedPhone) })}</Text>
      <Controller control={vm.codeForm.control} name="code" render={({ field, fieldState }) => (
        <TextField label={t('mobile.signIn.code')} value={field.value} onChangeText={v => field.onChange(v.replace(/\D/g, ''))} onBlur={field.onBlur}
          error={fieldState.error?.message ? t(fieldState.error.message as MessageKey) : undefined}
          keyboardType="number-pad" textContentType="oneTimeCode" autoComplete="sms-otp" maxLength={6} autoFocus
          style={[styles.code, styles.ltr]} onSubmitEditing={vm.onSubmitCode} />
      )} />
      <Button label={t('mobile.patientSignIn.resend')} variant="ghost" icon="refresh" onPress={vm.onResend} disabled={vm.busy} />
      {error}
    </Screen>
  );

  if (vm.step === 'who') return (
    <Screen>
      <Text variant="display" accessibilityRole="header">{t('mobile.patientSignIn.whoTitle')}</Text>
      <Text>{t('mobile.patientSignIn.whoIntro')}</Text>
      {vm.people.map(p => (
        <View key={p.id} style={[styles.option, p.linked && styles.optionLinked]}>
          <ListRow
            title={p.relation === 'self' ? t('mobile.patientSignIn.thisIsMe', { name: p.name }) : p.name}
            subtitle={p.linked ? t('mobile.patientSignIn.personLine', { clinic: p.clinic, since: p.since }) : t('mobile.patientSignIn.askToLink')}
            leading={<Avatar initials={p.initials} />}
            onPress={p.linked ? () => vm.onChoosePerson(p) : undefined}
          />
        </View>
      ))}
      <Banner icon="help-outline" tone="neutral" text={t('mobile.patientSignIn.notListed')} />
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
        <Text style={styles.center}>{t(vm.biometricAvailable ? 'mobile.patientSignIn.biometricBody' : 'mobile.patientSignIn.biometricNone')}</Text>
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  page: { paddingHorizontal: 8, paddingTop: 52, gap: 16 },
  mark: { alignSelf: 'flex-start' },
  chips: { flexDirection: 'row', flexWrap: 'wrap', gap: 8, marginTop: 4 },
  dots: { flexDirection: 'row', justifyContent: 'center', gap: 8, paddingBottom: 8 },
  dot: { width: 8, height: 8, borderRadius: 4, backgroundColor: theme.color.line },
  dotOn: { width: 24, backgroundColor: theme.color.patient700 },
  legal: { marginTop: 'auto' },
  ltr: { writingDirection: 'ltr' },
  code: { letterSpacing: 8, fontVariant: ['tabular-nums'] },
  option: { backgroundColor: theme.color.card, borderRadius: 16, borderWidth: 1, borderColor: theme.color.line, paddingHorizontal: 14 },
  optionLinked: { borderColor: theme.color.patient700, borderWidth: 1.5 },
  centered: { flexGrow: 1, alignItems: 'center', justifyContent: 'center', gap: 14, paddingHorizontal: 12 },
  center: { textAlign: 'center' },
});

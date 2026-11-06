import { useI18n, type MessageKey } from '@medvya/i18n';
import { Avatar, Banner, Card, Chip, Icon, IconButton, ListRow, Loading, Screen, Section, Stat, StateMessage, Text } from '@medvya/ui-native';
import { theme } from '@medvya/tokens';
import { I18nManager, StyleSheet, View } from 'react-native';
import type { PatientRecord } from '../domain/patients';

type Props = { record: PatientRecord | null; clinical: boolean; loading: boolean; notFound: boolean; error: string | null; onBack: () => void };

export function PatientRecordScreen(vm: Props) {
  const { t, formatDate } = useI18n();
  const back = (
    <View style={styles.bar}>
      <IconButton icon={I18nManager.isRTL ? 'arrow-forward' : 'arrow-back'} label={t('mobile.common.back')} onPress={vm.onBack} />
      <Text variant="heading" accessibilityRole="header">{vm.record?.name ?? t('mobile.record.title')}</Text>
    </View>
  );
  if (vm.loading) return <Screen header={back} scroll={false}><Loading label={t('mobile.common.loading')} /></Screen>;
  if (vm.error || vm.notFound || !vm.record) return (
    <Screen header={back} scroll={false}>
      <StateMessage icon="person-off" title={t('mobile.record.notFound')} body={vm.error ? t(vm.error as MessageKey) : undefined} />
    </Screen>
  );
  const r = vm.record;
  return (
    <Screen header={back}>
      <View style={styles.identity}>
        <Avatar initials={r.initials} size={56} />
        <View style={styles.flex}>
          <Text variant="title">{t('mobile.record.nameLine', { name: r.name, age: r.age, sex: t(`mobile.sex.${r.sex}`) })}</Text>
          <Text variant="caption">{t('mobile.record.idLine', { mrn: r.mrnLast4, dob: formatDate(new Date(`${r.dateOfBirth}T12:00:00Z`), 'UTC') })}</Text>
        </View>
      </View>

      {/* Allergies: always text and an icon, never colour alone (MDV-P5-034 check 4). */}
      {r.allergies.length > 0
        ? r.allergies.map(a => (
            <View key={a.substance} style={styles.allergy} accessible accessibilityRole="alert">
              <Icon name="medication" color={theme.color.danger700} />
              <Text variant="label" tone={theme.color.danger700} style={styles.flex}>
                {t('mobile.record.allergy', { substance: a.substance, reaction: a.reaction })}
              </Text>
              {a.verified ? <Chip label={t('mobile.record.verified')} tone="success" icon="verified" /> : null}
            </View>
          ))
        : <Banner tone="neutral" icon="check-circle-outline" text={t('mobile.record.noAllergies')} />}

      {vm.clinical ? <>
        {r.vitals.length > 0 ? (
          <View style={styles.stats}>
            {r.vitals.map(v => <Stat key={v.label} label={v.label} value={v.value} />)}
          </View>
        ) : null}

        <Section title={t('mobile.record.sinceLast')}>
          <Card style={styles.list}>
            {r.sinceLastVisit.length === 0 ? <Text variant="caption">{t('mobile.record.nothingNew')}</Text> : r.sinceLastVisit.map((f, i) => (
              <View key={f.text} style={i > 0 && styles.divider}>
                <ListRow title={f.text} trailing={f.status === 'verified'
                  ? <Chip label={t('mobile.record.verified')} tone="success" icon="verified" />
                  : <Chip label={t('mobile.record.aiUnreviewed')} tone="ai" icon="auto-awesome" />} />
              </View>
            ))}
          </Card>
        </Section>

        <Section title={t('mobile.record.problems')}>
          <Card>{r.problems.length ? r.problems.map(p => <Text key={p}>{p}</Text>) : <Text variant="caption">{t('mobile.record.noneRecorded')}</Text>}</Card>
        </Section>
        <Section title={t('mobile.record.medicines')}>
          <Card>{r.medicines.length ? r.medicines.map(m => <Text key={m}>{m}</Text>) : <Text variant="caption">{t('mobile.record.noneRecorded')}</Text>}</Card>
        </Section>
      </> : <Banner icon="lock-outline" text={t('mobile.record.clinicalOnly')} />}
    </Screen>
  );
}

const styles = StyleSheet.create({
  bar: { flexDirection: 'row', alignItems: 'center', gap: 12, paddingHorizontal: 16, paddingVertical: 8, backgroundColor: theme.color.card, borderBottomWidth: StyleSheet.hairlineWidth, borderBottomColor: theme.color.line },
  identity: { flexDirection: 'row', alignItems: 'center', gap: 14 },
  flex: { flex: 1 },
  allergy: { flexDirection: 'row', alignItems: 'center', gap: 10, padding: 14, borderRadius: 14, backgroundColor: theme.color.danger50 },
  stats: { flexDirection: 'row', gap: 10 },
  list: { paddingVertical: 4, gap: 0 },
  divider: { borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: theme.color.line },
});

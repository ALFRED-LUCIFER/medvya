import { useI18n, type MessageKey } from '@medvya/i18n';
import { AppHeader, Avatar, Card, ListRow, Loading, Screen, StateMessage, TextField, type IconName, type Tone } from '@medvya/ui-native';
import { theme } from '@medvya/tokens';
import { statuses } from '@medvya/tokens/status';
import { StyleSheet, View } from 'react-native';
import type { Session } from '../../session/domain/session';
import type { PatientFlag, PatientListItem } from '../domain/patients';

export const patientFlagLook: Record<PatientFlag, { tone: Tone; icon: IconName }> = {
  allergy: { tone: statuses.allergy.tone, icon: statuses.allergy.native },
  overdue: { tone: 'warning', icon: 'schedule' },
  new: { tone: 'info', icon: 'fiber-new' },
  critical: { tone: statuses.critical.tone, icon: statuses.critical.native },
};

type Props = {
  session: Session; query: string; onQuery: (q: string) => void; patients: PatientListItem[]; loading: boolean; error: string | null;
  onOpenAccount: () => void; onOpenPatient: (id: string) => void };

export function PatientsScreen(vm: Props) {
  const { t } = useI18n();
  return (
    <Screen header={<AppHeader title={t('mobile.staff.tab.patients')} subtitle={`${vm.session.facility.name} · ${vm.session.staff.name}`}
      initials={t('mobile.monogram')} action={{ icon: 'account-circle', label: t('mobile.account.title'), onPress: vm.onOpenAccount }} />}>
      <TextField label={t('mobile.patients.search')} placeholder={t('mobile.patients.searchHint')} value={vm.query} onChangeText={vm.onQuery}
        autoCorrect={false} returnKeyType="search" clearButtonMode="while-editing" />
      {vm.loading ? <Loading label={t('mobile.common.loading')} />
        : vm.error ? <StateMessage icon="cloud-off" title={t('mobile.common.unavailable')} body={t(vm.error as MessageKey)} />
        : vm.patients.length === 0 ? <StateMessage icon="person-search" title={t('mobile.patients.none')} body={t('mobile.patients.noneHint')} />
        : (
          <Card style={styles.list}>
            {vm.patients.map((p, i) => (
              <View key={p.id} style={i > 0 && styles.divider}>
                <ListRow
                  title={p.name}
                  subtitle={t('mobile.patients.line', { age: p.age, sex: t(`mobile.sex.${p.sex}`), last4: p.phoneLast4 })}
                  leading={<Avatar initials={p.initials} />}
                  chips={p.flag ? [{ label: t(`mobile.patients.flag.${p.flag}`), tone: patientFlagLook[p.flag].tone, icon: patientFlagLook[p.flag].icon }] : []}
                  onPress={() => vm.onOpenPatient(p.id)}
                />
              </View>
            ))}
          </Card>
        )}
    </Screen>
  );
}

const styles = StyleSheet.create({
  list: { paddingVertical: 4, gap: 0 },
  divider: { borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: theme.color.line },
});

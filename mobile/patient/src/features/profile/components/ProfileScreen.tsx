import { locales, ltr, useI18n, type Locale, type MessageKey } from '@medvya/i18n';
import { AppHeader, Avatar, Button, Card, Chip, Icon, ListRow, Loading, Screen, Section, StateMessage, Text } from '@medvya/ui-native';
import { theme } from '@medvya/tokens';
import { StyleSheet, View } from 'react-native';
import type { Person, Session } from '../../session/domain/session';
import type { Profile } from '../domain/profile';

type Props = {
  session: Session; phone: string; profile: Profile | null; family: Person[]; loading: boolean; error: string | null;
  biometric: boolean; locale: Locale; version: string; onLock: () => void; onSignOut: () => void;
};

// Changing choices, family access and downloads arrive with MDV-P5-037; here they are shown as they stand.
export function ProfileScreen(vm: Props) {
  const { t, formatDate } = useI18n();
  const divider = (i: number) => (i > 0 ? styles.divider : undefined);
  return (
    <Screen header={<AppHeader title={t('mobile.patient.tab.profile')} subtitle={`${vm.session.clinic.name} · ${vm.session.clinic.branch}`} initials={vm.session.person.initials} />}
      footer={<Button label={t('mobile.account.signOut')} variant="secondary" icon="logout" onPress={vm.onSignOut} />}>
      <View style={styles.identity}>
        <Avatar initials={vm.session.person.initials} size={64} />
        <Text variant="title">{vm.session.person.name}</Text>
        <Text variant="caption">{ltr(vm.phone)}</Text>
      </View>

      {vm.loading ? <Loading label={t('mobile.common.loading')} /> : null}
      {vm.error ? <StateMessage icon="cloud-off" title={t('mobile.common.unavailable')} body={t(vm.error as MessageKey)} /> : null}

      {vm.family.length > 0 ? <Section title={t('mobile.profile.family')}>
        <Card style={styles.list}>
          {vm.family.map((p, i) => (
            <View key={p.id} style={divider(i)}>
              <ListRow title={p.name} leading={<Avatar initials={p.initials} size={36} />}
                subtitle={p.id === vm.session.person.id ? t('mobile.profile.you')
                  : p.proxyUntil ? t('mobile.profile.proxyUntil', { date: formatDate(new Date(`${p.proxyUntil}T12:00:00Z`), 'UTC') })
                  : t('mobile.profile.notLinked')} />
            </View>
          ))}
        </Card>
      </Section> : null}

      {vm.profile ? (
        <Section title={t('mobile.profile.choices')}>
          <Text variant="caption">{t('mobile.profile.choicesNote')}</Text>
          <Card style={styles.list}>
            {vm.profile.choices.map((c, i) => (
              <View key={c.key} style={divider(i)}>
                <ListRow title={t(`mobile.profile.choice.${c.key}`)} subtitle={c.detail || undefined}
                  trailing={<Chip label={t(c.on ? 'mobile.profile.on' : 'mobile.profile.off')} tone={c.on ? 'success' : 'neutral'} icon={c.on ? 'check' : 'remove'} />} />
              </View>
            ))}
          </Card>
        </Section>
      ) : null}

      <Section title={t('mobile.account.security')}>
        <Card style={styles.list}>
          <View><ListRow title={t('mobile.profile.signIn')} subtitle={t(vm.biometric ? 'mobile.profile.signInBiometric' : 'mobile.profile.signInCode')} leading={<Icon name="lock-outline" />} /></View>
          <View style={styles.divider}><ListRow title={t('mobile.account.lockNow')} leading={<Icon name="lock-clock" />} onPress={vm.onLock} /></View>
        </Card>
      </Section>

      <Section title={t('mobile.account.preferences')}>
        <Card style={styles.list}>
          {vm.profile?.insurance ? <View><ListRow title={t('mobile.profile.insurance')} subtitle={vm.profile.insurance} leading={<Icon name="badge" />} /></View> : null}
          {/* shortcut: language follows the phone's setting; an in-app choice (with the RTL restart prompt) comes with MDV-P5-036. */}
          <View style={styles.divider}><ListRow title={t('app.language')} subtitle={locales.find(l => l.code === vm.locale)?.label} leading={<Icon name="translate" />} /></View>
          <View style={styles.divider}><ListRow title={t('mobile.profile.download')} subtitle={t('mobile.common.comingSoon')} leading={<Icon name="download" />} /></View>
        </Card>
      </Section>

      <Text variant="caption" style={styles.version}>{t('mobile.profile.version', { version: vm.version })}</Text>
    </Screen>
  );
}

const styles = StyleSheet.create({
  identity: { alignItems: 'center', gap: 6, paddingVertical: 8 },
  list: { paddingVertical: 4, gap: 0 },
  divider: { borderTopWidth: StyleSheet.hairlineWidth, borderTopColor: theme.color.line },
  version: { textAlign: 'center' },
});

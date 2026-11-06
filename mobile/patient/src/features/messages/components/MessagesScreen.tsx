import { useI18n, type MessageKey } from '@medvya/i18n';
import { AppHeader, Banner, IconButton, Loading, Screen, StateMessage, Text, TextField } from '@medvya/ui-native';
import { theme } from '@medvya/tokens';
import { StyleSheet, View } from 'react-native';
import type { Session } from '../../session/domain/session';
import type { Message } from '../domain/messages';

type Props = { session: Session; messages: Message[] | null; loading: boolean; error: string | null };

// Sending arrives with MDV-P5-037; the composer is shown disabled so the screen reads as it will work.
export function MessagesScreen(vm: Props) {
  const { t, formatTime } = useI18n();
  const header = <AppHeader title={vm.session.clinic.name} subtitle={t('mobile.messages.subtitle')} initials={vm.session.person.initials} />;
  return (
    <Screen header={header} footer={
      <View style={styles.composer}>
        <View style={styles.flex}>
          <TextField label={t('mobile.messages.write')} placeholder={t('mobile.messages.placeholder')} editable={false} hint={t('mobile.common.comingSoon')} />
        </View>
        <IconButton icon="send" label={t('mobile.messages.send')} disabled />
      </View>
    }>
      <Banner icon="info-outline" text={t('mobile.messages.notice')} />
      {vm.loading ? <Loading label={t('mobile.common.loading')} />
        : vm.error || !vm.messages ? <StateMessage icon="cloud-off" title={t('mobile.common.unavailable')} body={t((vm.error ?? 'mobile.error.generic') as MessageKey)} />
        : vm.messages.length === 0 ? <StateMessage icon="chat-bubble-outline" title={t('mobile.messages.none')} />
        : vm.messages.map(m => (
          <View key={m.id} style={[styles.bubble, m.from === 'me' ? styles.mine : styles.theirs]}>
            <Text tone={m.from === 'me' ? theme.color.card : theme.color.ink900}>{m.text}</Text>
            <Text variant="caption" tone={m.from === 'me' ? theme.color.patient50 : theme.color.ink600}>
              {t('mobile.messages.meta', { who: m.from === 'me' ? t('mobile.messages.you') : m.author, time: formatTime(new Date(m.at), vm.session.clinic.timeZone) })}
            </Text>
          </View>
        ))}
    </Screen>
  );
}

const styles = StyleSheet.create({
  composer: { flexDirection: 'row', alignItems: 'flex-end', gap: 10 },
  flex: { flex: 1 },
  bubble: { maxWidth: '85%', padding: 12, borderRadius: 18, gap: 4 },
  mine: { alignSelf: 'flex-end', backgroundColor: theme.color.patient700, borderBottomEndRadius: 4 },
  theirs: { alignSelf: 'flex-start', backgroundColor: theme.color.card, borderWidth: 1, borderColor: theme.color.line, borderBottomStartRadius: 4 },
});

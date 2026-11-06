import { useI18n } from '@medvya/i18n';
import { theme } from '@medvya/tokens';
import { StyleSheet, Text } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

export function HomeScreen() {
  const { t } = useI18n();
  return (
    <SafeAreaView style={styles.screen}>
      <Text accessibilityRole="header" style={styles.title}>{t('mobile.clinician.name')}</Text>
      <Text style={styles.body}>{t('mobile.home.ready')}</Text>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, padding: 24, gap: 8, backgroundColor: theme.color.surface },
  title: { fontSize: 28, fontWeight: '700', color: theme.color.brand700 },
  body: { fontSize: 16, color: theme.color.ink700 },
});

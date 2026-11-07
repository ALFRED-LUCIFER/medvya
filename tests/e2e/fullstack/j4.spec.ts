import { journey } from '../journeys/storyboard';

// MDV-P0-023: journey J4 (multilingual ward round with a clip, docs/02-design/journeys.md). Nothing of it is built yet: each step names its ticket.
journey('J4', {
  1: 'MDV-P6-002', // W-01 clip pairing and microphone self-test
  2: 'MDV-P6-003', // A-02 clip capture bound to the encounter and consent (recording consent: MDV-P3-003)
  3: 'MDV-P6-004', // W-02 encrypted offline buffer on the clip
  4: 'MDV-P3-010', // A-10 overlapping speech, unconfirmed speaker
  5: 'MDV-P3-009', // A-04 confirm three speakers
  6: 'MDV-P7-006', // A-05 sign with the HIS as authority, write-back queued (note review: MDV-P3-012)
});

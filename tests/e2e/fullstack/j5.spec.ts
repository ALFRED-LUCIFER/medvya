import { journey } from '../journeys/storyboard';

// MDV-P0-023: journey J5 (delayed lab result escalation, docs/02-design/journeys.md). Nothing of it is built yet: each step names its ticket.
journey('J5', {
  1: 'MDV-P2-018', // E-05 investigation order, Draft → Ordered
  2: 'MDV-P4-014', // C-05 inbound report, low-confidence match confirmed by a person
  3: 'MDV-P4-015', // C-01 received is not reviewed
  4: 'MDV-P4-018', // C-04 overdue escalation
  5: 'MDV-P4-015', // C-01 mark reviewed, with actor and time
  6: 'MDV-P4-019', // C-06 contact the patient (contact log: MDV-P4-017)
  7: 'MDV-P4-016', // C-02 close with evidence
});

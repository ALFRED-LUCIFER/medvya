import { journey } from '../journeys/storyboard';

// MDV-P0-023: journey J6 (hospital with an HIS, failed write-back, docs/02-design/journeys.md). Nothing of it is built yet: each step names its ticket.
journey('J6', {
  1: 'MDV-P7-002', // H-08 record authority per facility
  2: 'MDV-P7-006', // A-05 signed note shows "Not yet in HIS" (signing: MDV-P2-012)
  3: 'MDV-P7-006', // H-01 HIS 503 three times
  4: 'MDV-P7-006', // H-01 retry, delivered with a receipt (console: MDV-P7-005)
  5: 'MDV-P7-006', // H-04 conflict routed to the clinician
  6: 'MDV-P7-008', // H-05 append-only audit trail of the write-back
});

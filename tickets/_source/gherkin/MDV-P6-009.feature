Feature: Outbound voice reminder and recall calls; waitlist fill from cancellations
  As a receptionist, I want automated reminder and waitlist calls, so that patients without WhatsApp are reached and freed slots are filled.

  # chunk: Place reminder and recall calls only with opt-in
  @todo
  Scenario: Calls only to patients who opted in
    Given a patient without call opt-in and a patient with call opt-in
    When reminder and recall calls are placed
    Then only the opted-in patient is called

  @todo
  Scenario: Outcomes are logged and never close care tasks
    Given a call has finished
    When the outcome is recorded
    Then it is logged as a contact attempt
    And no care task is closed by the call itself

  # chunk: Fill freed slots from the waitlist in order
  @todo
  Scenario: Waitlist offers go in order and book only on confirmation
    Given a slot frees and several patients are waitlisted
    When offers are made
    Then slots are offered in waitlist order
    And a slot is booked only on explicit confirmation

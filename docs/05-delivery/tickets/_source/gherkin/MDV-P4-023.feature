Feature: Portal booking and reschedule with fee and cancellation policy shown (R-02)
  As a patient, I want to book and reschedule online seeing fee and cancellation policy, so that there are no surprises.

  # chunk: Add portal booking on the shared booking service
  @todo
  Scenario: Same concurrency-safe path
    Given the 100-parallel booking test
    When it includes the portal channel
    Then there is no double booking
  @todo
  Scenario: Source recorded
    Given a portal booking
    When it is saved
    Then the booking source is recorded as portal for the ≥ 30 % metric (exit gate 4)

  # chunk: Add fee policy and private reason
  @todo
  Scenario: Fee and policy before confirm
    Given a booking
    When the patient is about to confirm
    Then the fee and cancellation policy are shown
    And late cancellations follow the policy
  @todo
  Scenario: Reason encrypted
    Given a free-text reason
    When it is saved
    Then it is stored encrypted and shown only to clinicians

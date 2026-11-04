Feature: WhatsApp reminders and confirmations (EN/AR/HI), opt-in records, delivery status, one-tap reschedule, "doctor running late" broadcast (N-03, design needed)
  As a patient, I want appointment messages on WhatsApp in my language, so that I can confirm or reschedule easily.

  # chunk: Add approved templates and opt-in gating
  @todo
  Scenario: Approved templates only
    Given a message to send
    When it is validated
    Then only approved templates are sent
    And no clinical details are carried
  @todo
  Scenario: Opt-in required, opt-out honoured
    Given a patient
    When no WhatsApp opt-in receipt exists
    Then no message is sent
    And opt-out via reply is honoured
  @todo
  Scenario: Delivery status and fallback
    Given a sent message
    When delivery status arrives
    Then the notification record is updated
    And failures fall back to SMS if configured

  # chunk: Add running-late broadcast and confirmation metric
  @todo
  Scenario: Running-late broadcast
    Given a doctor running late
    When the broadcast is sent
    Then it reaches only patients booked with that doctor today
    And is audited
  @todo
  Scenario: Confirmations counted
    Given a patient confirms via WhatsApp
    When the metric is computed
    Then it counts toward the ≥ 30 % metric (exit gate 4)

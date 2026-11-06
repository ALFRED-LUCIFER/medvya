Feature: Register India DLT sender ID and SMS templates (and UAE SMS sender if applicable)
  As a founder, I want SMS sender and templates registered, so that SMS reminders can be sent legally in India.

  # chunk: Register DLT sender and templates
  @todo
  Scenario: Entity, sender and templates approved
    Then entity, header (sender) and all P1 templates are approved with template IDs recorded

  @todo
  Scenario: SMS provider connected in staging
    Then an SMS provider contract or account exists and is connected in staging

  @todo
  Scenario: Email-only fallback is documented
    Then a fallback (email only) is documented if approval slips

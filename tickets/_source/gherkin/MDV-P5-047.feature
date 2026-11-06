Feature: Service requests: Help in the clinic app, support inbox with SLA, internal notes, diagnostics
  As practice owner, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Service requests: Help in the clinic app, support inbox with SLA, inte
  @todo
  Scenario: Check 1
    Given a screenshot shows patient data
    When it is attached
    Then the patient data is blurred before storage
  @todo
  Scenario: Check 2
    Given support
    When asks for access
    Then nothing is visible until the owner approves and it ends after 60 minutes
  @todo
  Scenario: Check 3
    Given a request breaches its SLA
    When time passes
    Then it is flagged to the team lead

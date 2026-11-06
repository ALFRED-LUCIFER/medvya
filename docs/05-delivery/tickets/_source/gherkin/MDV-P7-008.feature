Feature: Audit log and break-glass review (H-05)
  As hospital IT, a privacy officer or a doctor, I want controlled break-glass access and a searchable audit log, so that emergency access is accountable.

  # chunk: Grant break-glass access with justification and expiry
  @todo
  Scenario: Break-glass needs justification and expires
    Given a doctor without access to a chart
    When they request break-glass with a typed justification
    Then temporary read is granted with a banner and a short expiry
    And it expires automatically and appears as "Pending review"

  # chunk: Review break-glass use and alert on repeats
  @todo
  Scenario: Reviewers record outcomes
    Given a break-glass event in the review queue
    When a reviewer records an outcome
    Then the outcome is saved against the event

  @todo
  Scenario: Repeated break-glass alerts the privacy officer
    Given a user has used break-glass repeatedly
    When the repeat threshold is reached
    Then the privacy officer is alerted

  # chunk: Explore the audit log with opaque patient refs
  @todo
  Scenario: Audit explorer filters show no PHI
    Given audit events
    When a reviewer filters by actor, patient ref, action or purpose
    Then matching events are shown with opaque patient refs
    And no PHI appears in logs

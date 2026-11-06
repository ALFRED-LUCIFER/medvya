Feature: Feature rollouts: staged release (internal, beta tenants, percentage, everyone), kill switch and clinical sign-off gate
  As medvya operations, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Feature rollouts: staged release (internal, beta tenants, percentage, 
  @todo
  Scenario: Check 1
    Given a clinical flag
    When is moved to everyone without sign-off
    Then the change is refused
  @todo
  Scenario: Check 2
    Given the kill switch
    When is used
    Then the feature disappears for all tenants and no data is deleted
  @todo
  Scenario: Check 3
    Given a flag change
    When is made
    Then it is audited with a reason

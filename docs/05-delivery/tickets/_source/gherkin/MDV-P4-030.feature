Feature: Clinic reports and owner dashboard: appointments, no-shows, revenue, follow-up completion, note time saved (H-07)
  As a practice owner, I want a clinic dashboard, so that I can see how the practice is doing.

  # chunk: Add KPI dashboard with honest labelling
  @todo
  Scenario: Documentation time labelled estimate
    Given the documentation time KPI
    When shown
    Then it is labelled as an estimate with the method stated
    And makes no clinical outcome claims
  @todo
  Scenario: Figures reconcile
    Given a sample month
    When the figures are compared with source data
    Then they reconcile

  # chunk: Add role-aware drill-down and owner walkthrough
  @todo
  Scenario: Drill-down respects roles
    Given admins and clinicians
    When they drill down
    Then admins see counts and clinicians see their patients
  @todo
  Scenario: Owner walkthrough delivered
    Given each pilot owner
    When the walkthrough session is held
    Then it is delivered (delivery package 2)

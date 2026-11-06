Feature: Performance: draft ready ≤ 60 s (p50) after recording stops
  As a developer, I want drafts ready within 60 seconds of stopping, so that doctors do not wait.

  # chunk: Measure and alert on draft latency
  @todo
  Scenario: p50 target on staging
    Given 10-minute consultations on staging
    When latency is measured
    Then p50 is 60 s or less and p90 is recorded
  @todo
  Scenario: Dashboard and alert
    Given the latency dashboard
    When checked
    Then a dashboard and an alert exist, with no PHI

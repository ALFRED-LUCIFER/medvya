Feature: Performance budget and load test for booking and calendar
  As a developer, I want measured performance budgets, so that booking and calendar stay fast for two clinics.

  # chunk: Add load test and nightly regression budget
  @todo
  Scenario: Latency budgets under the load profile
    Given the load profile on staging
    Then booking API p95 is under 300 ms
    And reads p95 are under 300 ms and writes under 600 ms

  @todo
  Scenario: Calendar first render budget
    Given a typical clinic PC profile over 4G throttling
    Then calendar first render is under 2.5 s

  @todo
  Scenario: Nightly job alerts on regression
    When a nightly measurement regresses by more than 20 %
    Then the job fails and an alert is raised

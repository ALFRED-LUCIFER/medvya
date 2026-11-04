Feature: Accessibility audit and load test before GA
  As a developer, I want accessibility and load verified, so that GA is safe for all users.

  # chunk: Complete accessibility audit and load test
  @todo
  Scenario: No AA blockers
    Given the staff app and portal
    When audited
    Then no WCAG AA blockers remain on core journeys
  @todo
  Scenario: Latency under 5× load
    Given 5× pilot load
    When tested
    Then reads p95 < 300 ms, writes < 600 ms and page interactive < 2.5 s p75

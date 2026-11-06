Feature: Full enterprise analytics (throughput, open loops, adoption) with authorised drill-down (H-07)
  As a hospital admin, I want cross-facility analytics with authorised drill-down, so that I can manage throughput and adoption.

  # chunk: Aggregate metrics across facilities with scoped drill-down
  @todo
  Scenario: Metrics aggregate across facilities with filters
    Given several facilities
    When the hospital admin views H-07
    Then throughput, open loops and adoption metrics aggregate across facilities
    And per-facility filters narrow them, with estimate caveats shown

  @todo
  Scenario: Drill-down obeys facility and role scope
    Given a user with limited facility and role scope
    When they drill into a metric
    Then only authorised detail is shown

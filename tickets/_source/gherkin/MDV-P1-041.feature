Feature: Baseline metrics capture and in-product measurement (check-in time, no-shows, double bookings, reminder delivery)
  As a practice owner, I want baseline and live metrics, so that the pilot can show real improvement.

  # chunk: Capture baselines and add owner metrics
  @todo
  Scenario: Baselines are recorded
    Then baseline values per clinic are recorded with method and dates

  @todo
  Scenario: Median check-in time is computed
    Given pilot weeks of event timestamps
    Then the median check-in time for returning patients is computed
    And it is 60 s or less, or the gap is reported (exit gate 3)

  @todo
  Scenario: Metrics have no PHI and owners can see them
    Then the metrics contain no PHI
    And they are visible to owners

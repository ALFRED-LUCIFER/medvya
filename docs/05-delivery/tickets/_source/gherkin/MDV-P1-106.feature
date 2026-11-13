Feature: Check-in time is measured from opening Walk-in to the token, so the pilot metric cannot read 0 s, target met
  A walk-in is created and checked in by one action, so its timed check-in is zero and the metric reads Check-in time, returning patients (median): 0 s.

  # chunk: Check-in time is measured from opening Walk-in to the token, so the pilot metric cannot read 0 s, target met
  @todo
  Scenario: A walk-in has a real time
    Given a walk-in checked in 40 seconds after Walk-in was opened
    When the metric is read
    Then that check-in counts as about 40 s

  @todo
  Scenario: Zero is never invented
    Given a check-in with no measured start
    When the median is computed
    Then it is left out and shown as not measured

  @todo
  Scenario: The rule is proven
    Given the metric test
    When unmeasured rows are counted as zero again
    Then the test fails

  @todo
  Scenario: Honest empty state
    Given no timed check-ins in the period
    When the metric renders
    Then it says No data yet and not a number

  @todo
  Scenario: Walk-in not longer
    Given the walk-in path
    When it is measured
    Then it is still at most 6 clicks new and 3 returning

  @todo
  Scenario: Scoped and clean
    Given two clinics
    When metrics are read
    Then each sees only its own; no patient data is in the metric, logs or address

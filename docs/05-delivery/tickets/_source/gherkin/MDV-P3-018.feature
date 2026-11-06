Feature: De-identified evaluation set and CI gate before any prompt or model change
  As a developer or clinical advisor, I want an evaluation gate on prompt and model changes, so that no fabricated medicine or dose ships.

  # chunk: Build the evaluation set
  @todo
  Scenario: Reviewed set without real data
    Given the evaluation set of at least 50 consultations
    When reviewed
    Then ground-truth notes are reviewed by the clinical advisor and contain no real patient data

  # chunk: Add the CI gate and report
  @todo
  Scenario: Gate blocks fabrication
    Given a change to prompts, models or schema
    When CI runs
    Then the gate runs automatically and blocks on any fabricated medicine or dose (exit gate 1 requires 0)
  @todo
  Scenario: Metrics reported separately
    Given the evaluation report
    When read
    Then metrics are separate, not a single score
  @todo
  Scenario: Results versioned
    Given evaluation results
    When stored
    Then they are versioned with prompt hash and model version

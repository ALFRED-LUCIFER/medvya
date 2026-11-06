Feature: Design the 11 new P5 screens (N-04 … N-14) before build
  As the founder or designer, I want approved designs for the new P5 screens, so that builds start from agreed designs.

  # chunk: Design the migration and data-quality screens
  @todo
  Scenario: Designs with states
    Given screens N-04 to N-14
    When designs are reviewed
    Then each N-screen has an approved design with states (empty, loading, error, 403, offline)
    And IN/AE variants where relevant

  # chunk: Import designs and update specs
  @todo
  Scenario: Designs imported
    Given approved designs
    When imported
    Then they are in design/ and screen-specs are updated
  @todo
  Scenario: Confidence re-assessed
    Given the dependent tickets
    When designs are done
    Then their confidence is re-assessed and raised to ≥ 75 % where resolved

Feature: Full clinic data export in FHIR and CSV (no lock-in) (N-14)
  As a practice owner, I want to export all clinic data, so that I am never locked in.

  # chunk: Add secured export
  @todo
  Scenario: Step-up auth and expiry
    Given an export request
    When made
    Then it requires owner step-up auth and is audited
    And the link expires

  # chunk: Add FHIR and CSV validation
  @todo
  Scenario: FHIR validation
    Given synthetic data
    When exported
    Then the export validates against FHIR R4 base profiles in CI
  @todo
  Scenario: CSV re-import reproduces counts
    Given the CSV export
    When re-imported into a fresh test tenant
    Then counts are reproduced

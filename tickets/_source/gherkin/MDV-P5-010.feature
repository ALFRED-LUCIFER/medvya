Feature: UAE claims via eClaimLink (Dubai) / Shafafiya (Abu Dhabi) for the launch emirate (P-14)
  As a practice admin, I want UAE claims submitted through the regulator platform, so that insurers pay.

  # chunk: Add launch-emirate claims adapter
  @todo
  Scenario: Conformance passed
    Given the regulator conformance tests
    When run
    Then they pass and evidence is stored
  @todo
  Scenario: Round-trip in test environment
    Given a claim
    When submitted in the regulator test environment
    Then it round-trips with remittance matched

  # chunk: Add scope deferral rule
  @todo
  Scenario: Deferral without blocking India
    Given onboarding is not complete by week 31
    When the date passes
    Then UAE launch scope is formally deferred without blocking an India launch

Feature: Store release: accounts, privacy labels, MASVS test, accessibility and RTL checks, EAS Update policy
  As founder, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Store release: accounts, privacy labels, MASVS test, accessibility and
  @todo
  Scenario: Check 1
    Given a build
    When it is submitted
    Then privacy labels match the data map
  @todo
  Scenario: Check 2
    Given the MASVS review
    When it finishes
    Then no open high or critical finding
  @todo
  Scenario: Check 3
    Given an OTA update
    When it contains clinical logic
    Then the pipeline refuses it

Feature: Insurance coverage and live eligibility with co-pay estimate at the desk (P-08)
  As a receptionist, I want to check insurance eligibility at the desk, so that the patient sees a co-pay estimate.

  # chunk: Add eligibility check with manual fallback
  @todo
  Scenario: Result stores provenance
    Given an eligibility check
    When saved
    Then it stores source, reference, timestamp and the user who checked
  @todo
  Scenario: Adapter failure fallback
    Given an adapter failure
    When it occurs
    Then manual entry is available without blocking care

  # chunk: Add co-pay estimate display
  @todo
  Scenario: Co-pay estimate shown
    Given an eligibility result
    When billing or the UAE portal insurance card is viewed
    Then the co-pay estimate appears

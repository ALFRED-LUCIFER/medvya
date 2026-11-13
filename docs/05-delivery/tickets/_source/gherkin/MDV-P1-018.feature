Feature: Patient directory and privacy-safe search for the front desk
  As a receptionist, I want to find patients without exposing full details, so that privacy is kept while I book and check in.

  # chunk: Add privacy-safe patient search and directory
  @done
  Scenario: Exact identifier search only
    When a receptionist searches by full phone number or exact EID or ABHA
    Then the matching patients are returned
    And partial ID search is not allowed

  @done
  Scenario: Result rows are minimal and opening is audited
    When search results are shown
    Then rows show the full name, age, sex and the MRN and phone tails
    And full demographics appear only after opening, which writes an audit event

  @done
  Scenario: Empty state
    When no patient matches
    Then "No patients match. Register a new patient" is shown

  @done
  Scenario: Search performance and rate limit
    Given 50,000 synthetic patients
    Then search p95 is under 300 ms
    And the endpoint is rate-limited

Feature: Legal review of clinical data processing before the first real note
  As a founder, I want counsel to review clinical data handling, so that no real note is created before it is lawful.

  # chunk: Obtain legal sign-off and load retention periods
  @todo
  Scenario: Sign-off before real patients
    Given clinical features
    When enabling them for real patients
    Then written sign-off exists and the flag stays off until then
  @todo
  Scenario: Retention periods loaded
    Given each country
    When retention periods for notes, prescriptions and documents are confirmed
    Then they are loaded into retention configuration

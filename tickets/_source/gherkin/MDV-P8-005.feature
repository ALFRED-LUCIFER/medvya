Feature: Remaining UAE health exchanges (the two not used at launch) (N-07)
  As a doctor or practice admin, I want the remaining UAE exchanges, so that each facility connects to its own emirate's exchange.

  # chunk: Resolve regulator onboarding for each exchange
  @todo
  Scenario: Regulator onboarding is complete for each added exchange
    Given the two exchanges of Malaffi, NABIDH and Riayati not used at launch
    When onboarding is done with each regulator
    Then conformance is passed for each added exchange

  # chunk: Configure the exchanges in the P5 exchange module
  @todo
  Scenario: Facility emirate selects its exchange
    Given a facility in an emirate whose exchange is not the launch one
    When the facility exchange is resolved
    Then the exchange configured for that emirate is selected


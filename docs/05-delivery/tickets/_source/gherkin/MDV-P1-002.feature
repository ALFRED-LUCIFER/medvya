Feature: Country and data-residency selector that binds a new tenant to one data plane
  As a solo doctor or practice owner, I want to choose where I practise, so that my tenant is created only in that country's deployment.

  # chunk: Add residency selector with translated explanation and waitlist
  @done
  Scenario: Residency explanation matches the design and is translated
    When a visitor opens the "Where do you practise?" page
    Then the residency explanation text matches the design
    And it is available in English, Hindi and Arabic

  @done
  Scenario: Country not yet live shows a waitlist
    Given the selected country deployment is not live
    When the visitor continues
    Then a waitlist form with no PHI is shown instead of sign-up

  # chunk: Bind tenant to its country deployment and record selection in audit trail
  @todo
  Scenario: Tenant is created only in the selected country's deployment
    Given a visitor selects a live region and continues
    When the tenant is created
    Then it exists only in that country's deployment

  @todo
  Scenario: Selection is recorded in the onboarding audit trail
    When a user selects a region and continues
    Then the selection is recorded in the onboarding audit trail

  # chunk: Lock country and residency after creation
  @done
  Scenario: Country and residency are immutable after creation
    Given a tenant exists
    When anything tries to change its country_code or residency_region
    Then the change is rejected

Feature: Sign pilot agreements with two partner clinics
  As a founder, I want signed pilot agreements, so that production tenants can be created for two clinics.

  # chunk: Sign pilot agreements
  @todo
  Scenario: Two signed agreements are on file
    Then two signed pilot agreements including a DPA are on file before production tenants are created

  @todo
  Scenario: Each clinic names contacts
    Then each clinic names an owner contact and a receptionist champion

  @todo
  Scenario: Start dates are scheduled
    Then pilot start dates are scheduled with each clinic, considering the year-end holiday

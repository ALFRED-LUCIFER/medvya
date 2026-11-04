Feature: Production deployment for the launch country and a production tenant per pilot clinic
  As a developer, I want production deployed and a tenant per pilot clinic, so that clinics can start on R1.

  # chunk: Deploy production and create pilot tenants
  @todo
  Scenario: Production passes the 12-layer checklist
    Then production passes the 12-layer checklist with evidence

  @todo
  Scenario: Each clinic has its own tenant
    Then each partner clinic has its own tenant
    And owners signed in with MFA and accepted ToS and DPA

  @todo
  Scenario: Only R1 features are enabled
    Then only R1 features are enabled by flags
    And clinical modules are not reachable

  @todo
  Scenario: No synthetic data in production
    When the production check runs
    Then no synthetic data is present

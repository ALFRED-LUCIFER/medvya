Feature: Clinical safety case and hazard log with a named clinical safety reviewer
  As a clinical advisor or founder, I want a hazard log and named safety reviewer, so that each release is clinically safety-signed.

  # chunk: Publish the hazard log, reviewer and incident process
  @todo
  Scenario: Hazard log covers P2 features
    Given the P2 clinical features
    When the hazard log is reviewed
    Then every feature has at least one mitigation linked to a test or control
  @todo
  Scenario: Named reviewer signs R2
    Given the R2 release
    When released
    Then a named clinical safety reviewer is recorded and signs it
  @todo
  Scenario: Incident review process tested
    Given the documented process (report, triage, fix, review)
    When a tabletop exercise is run
    Then it has been tested once

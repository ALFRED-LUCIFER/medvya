Feature: R0 exit gate review and release notes (internal platform)
  As a founder, I want the R0 exit gate reviewed with evidence, so that the platform is handed over with confidence.

  # chunk: Review exit criteria evidence and publish release notes
  @todo
  Scenario: Each exit criterion has linked evidence
    Then each P0 exit criterion has linked evidence
    And the evidence is the journeys sign-off record, the timed restore drill, the CI run with the cross-tenant RLS test and the staging deploy log from a tag on protected main

  @todo
  Scenario: Release notes list scope and exclusions
    Then R0 release notes list delivered requirement IDs (P0-01 to P0-10) and known issues
    And they state what is not included (no real patient data, no public website, no booking)

  @todo
  Scenario: Delivery package is handed over
    Then the delivery package is handed to the founder and clinical advisor

  @todo
  Scenario: No open serious security findings
    Then there are no open high or critical security findings, or each is explicitly accepted with a dated plan

Feature: Migration mapping: AI-proposed column and code mappings (LOINC, ICD-10, formulary) approved by a person; reusable vendor templates (N-04)
  As Medvya operations, I want AI-proposed mappings approved by a person, so that migrated data is coded correctly.

  # chunk: Add human-approved mappings
  @todo
  Scenario: Approval required
    Given a proposed mapping
    When it has no human approval
    Then it is not applied
  @todo
  Scenario: Approval recorded
    Given an approved mapping
    When it is saved
    Then the actor and time are recorded

  # chunk: Add vendor templates and unmapped list
  @todo
  Scenario: Reusable templates
    Given an approved template for a vendor
    When applied to a new clinic on the same vendor
    Then it can be diffed for changes
  @todo
  Scenario: Unmapped values listed
    Given values with no mapping
    When the mapping is reviewed
    Then they are listed for manual decision
    And nothing is silently dropped

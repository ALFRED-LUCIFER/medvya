Feature: Price list import tool (billing setup service)
  As Medvya operations, I want to import price lists with a validation preview, so that clinics are set up quickly and safely.

  # chunk: Add price list import with preview and commit
  @todo
  Scenario: Invalid rows blocked until approved
    Given a file with invalid rows
    When the preview runs
    Then each invalid row is reported with a reason
    And nothing is committed until the preview is approved
  @todo
  Scenario: Idempotent import records importer
    Given an import is approved
    When the same file is imported again
    Then services are matched by service code with no duplicates
    And the importer is recorded
  @todo
  Scenario: Templates provided
    Given the import screen
    When the user looks for templates
    Then template files for India and UAE are provided

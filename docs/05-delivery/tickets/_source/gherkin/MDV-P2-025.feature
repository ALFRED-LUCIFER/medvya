Feature: Note template engine and specialty template pack for the partner clinics
  As a doctor, I want versioned specialty note templates, so that notes fit my specialty.

  # chunk: Add versioned templates and the base pack
  @todo
  Scenario: Six base templates reviewed
    Given the template pack
    When reviewed
    Then six base templates exist and are reviewed by the clinical advisor
  @todo
  Scenario: Pilot specialty templates configured
    Given each pilot doctor
    When R2 starts
    Then their specialty templates are configured
  @todo
  Scenario: Versioning keeps old notes intact
    Given a template change
    When saved
    Then a new version is created and existing notes keep the version they used

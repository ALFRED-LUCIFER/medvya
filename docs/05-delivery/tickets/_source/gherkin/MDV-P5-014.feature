Feature: Patient report upload with AI extraction marked "Unverified · patient upload" until accepted (N-08)
  As a patient, I want to upload reports before a visit, so that my doctor can review them.

  # chunk: Add patient upload with safety checks
  @todo
  Scenario: Upload safety
    Given an upload
    When received
    Then it is malware-scanned and size/type limited
    And prompt-injection content in documents is ignored

  # chunk: Add unverified extraction with source links
  @todo
  Scenario: Not verified until accepted
    Given extracted values
    When no clinician has accepted them
    Then they never enter verified trends
  @todo
  Scenario: Links to original
    Given an extracted value
    When opened
    Then it links to the page or region of the original file

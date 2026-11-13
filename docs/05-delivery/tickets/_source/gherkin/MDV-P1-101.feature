Feature: Patient picker rows show date of birth and last visit, and mark possible duplicates
  Two Deepak Nair, 47 y, Male rows differed only in the last four MRN digits; a hurried person picks the first, which was the duplicate.

  # chunk: Patient picker rows show date of birth and last visit, and mark possible duplicates
  @todo
  Scenario: Same names differ visibly
    Given two patients with the same name, age and sex
    When the picker lists them
    Then the rows differ in at least two shown fields (date of birth, last visit or the duplicate mark)

  @todo
  Scenario: Duplicates are marked in words
    Given a record with an open merge request
    When it is listed
    Then it reads Possible duplicate and the older record is first and reads Main record

  @todo
  Scenario: No patient data in addresses
    Given a search
    When the address bar, logs and telemetry are read
    Then they hold no name, phone digits or date of birth

  @todo
  Scenario: Reveal stays logged
    Given Show full numbers
    When it is used
    Then an audit row is written as today

  @todo
  Scenario: Authorisation unchanged and proven
    Given a role that may not search clinical data
    When the search endpoint is called
    Then the answer holds only the desk fields, and the test fails if last visit exposes a diagnosis or note text

  @todo
  Scenario: Rows are comfortable and accessible
    Given a touch device
    When rows are measured
    Then each is at least 48 px tall and the whole row is the target; axe finds no violation in English or Arabic

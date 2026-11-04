Feature: Release rules: the doctor controls what patients see and when
  As a doctor, I want to control what patients see and when, so that results are never shown before I decide.

  # chunk: Add release records and policies
  @todo
  Scenario: Release record required
    Given an item with no release record
    When the portal is opened
    Then it is not visible
    And abnormal results cannot be auto-released
  @todo
  Scenario: Release audited and timely
    Given a release or unrelease
    When it is done
    Then it is audited and reflected in the portal within 1 minute
  @todo
  Scenario: Default policies documented
    Given the tenant defaults
    When reviewed
    Then they are configurable, documented and reviewed by the clinical advisor

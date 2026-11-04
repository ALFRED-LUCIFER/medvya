Feature: Define and operate the doctor licence verification process
  As Medvya operations, I want a licence verification runbook, so that only verified doctors sign real notes.

  # chunk: Publish runbook and verify pilot licences
  @todo
  Scenario: Runbook published and linked
    Given the runbook
    When checked
    Then docs/runbooks/licence-verification.md exists and is linked from F-09
  @todo
  Scenario: Pilot licences verified with evidence
    Given each pilot doctor
    When they are about to sign real notes
    Then their licence is verified with recorded evidence first

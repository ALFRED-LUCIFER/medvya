Feature: R3 pilot with 5–10 opt-in clinicians, exit gate review and release notes
  As a founder or clinical safety reviewer, I want an R3 exit gate review, so that AI scribe ships only with evidence and a working kill switch.

  # chunk: Run the R3 exit gate review
  @todo
  Scenario: Evidence for all exit criteria
    Given the R3 pilot
    When the gate review is held
    Then evidence for all four exit criteria is attached and confirmed
  @todo
  Scenario: Kill switch verified
    Given go-live
    When checked
    Then the per-tenant kill switch has been verified
  @todo
  Scenario: Release notes content
    Given the R3 release notes
    When published
    Then they include the evaluation report and exclusions (clip, earpiece assistant, mixed Arabic/Hindi/Malayalam notes)
  @todo
  Scenario: Journeys pass with stub AI
    Given a stubbed AI provider
    When Playwright J1 (consent declined) and J2 (consent to sign) run
    Then they pass

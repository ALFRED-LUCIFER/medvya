Feature: Device kit, quick-start guide, fleet guide and R6 gate
  As a founder or practice admin, I want a documented device kit and verified exit criteria, so that the R6 clip release can ship.

  # chunk: Ship guides, kits and R6 evidence
  @todo
  Scenario: Guides published and kits delivered
    Given the quick-start and fleet guides (assign, firmware rings, revoke)
    When R6 is prepared
    Then both guides are published
    And kits (clip, charger, quick-start) are delivered to pilot doctors

  @todo
  Scenario: Exit evidence and release notes are complete
    Given the R6 exit criteria
    When the gate is reviewed
    Then evidence is attached for each criterion
    And the R6 notes list the exclusions, including autonomous triage and diagnosis

  @todo
  Scenario: Journey J4 passes
    Given the ward round with clip journey J4, with a device simulator where needed
    When the Playwright or device test runs
    Then J4 passes

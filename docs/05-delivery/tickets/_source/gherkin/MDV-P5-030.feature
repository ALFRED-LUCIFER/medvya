Feature: v1.0 general availability gate and release notes
  As the founder, I want the GA gate reviewed, so that v1.0 ships with evidence.

  # chunk: Review GA gate and publish release
  @todo
  Scenario: Exit criteria evidenced
    Given the P5 exit criteria
    When reviewed
    Then all four are evidenced and confirmed
  @todo
  Scenario: Release notes and tag
    Given v1.0
    When released
    Then release notes are published and version v1.0.0 is tagged from main
  @todo
  Scenario: Journeys pass
    Given the GA build on staging
    When journeys J1–J5 run
    Then they pass end-to-end

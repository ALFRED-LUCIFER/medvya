Feature: Print, accessibility and zoom verification across clinical screens
  As a developer, I want print and accessibility verified across clinical screens, so that P2 meets its accessibility requirements.

  # chunk: Verify accessibility, keyboard path and print outputs
  @todo
  Scenario: axe and screen-reader pass
    Given the P2 screens
    When axe runs and a manual screen-reader pass is done
    Then there are no violations and the pass is recorded for N-01, E-07 and the sign modal
  @todo
  Scenario: Full keyboard path
    Given a keyboard only user
    When they go Today, Patient 360, note, sign, Rx authorise, print
    Then the whole path works
  @todo
  Scenario: Print outputs match designs
    Given the printed outputs
    When compared with the designs
    Then they match and include identifiers on every page

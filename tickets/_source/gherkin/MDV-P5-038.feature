Feature: Private push notifications for both apps (IDs only, no health text on the lock screen)
  As developer, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Private push notifications for both apps (IDs only, no health text on 
  @todo
  Scenario: Check 1
    Given a result is ready
    When a push is sent
    Then the payload holds an ID and the fixed text and no test, doctor speciality or result
  @todo
  Scenario: Check 2
    Given the user turns notifications off
    When an event happens
    Then no push is sent and the in-app list still shows it
  @todo
  Scenario: Check 3
    Given a push token is revoked
    When an event happens
    Then no push goes to that device

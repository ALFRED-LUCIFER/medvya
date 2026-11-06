Feature: Mobile ADR and threat model: two Expo store apps (Medvya Clinic, Medvya for patients) from one repo
  As founder, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Mobile ADR and threat model: two Expo store apps (Medvya Clinic, Medvy
  @todo
  Scenario: Check 1
    Given the ADR
    When it is accepted
    Then it names the two apps, the repo folders, the auth flow and the on-device storage rule
  @todo
  Scenario: Check 2
    Given the threat model
    When it is reviewed
    Then lost phone, shared phone, screenshot, push preview and offline queue each have a control
  @todo
  Scenario: Check 3
    Given the data map
    When it is updated
    Then device storage and push payloads are listed with their classification

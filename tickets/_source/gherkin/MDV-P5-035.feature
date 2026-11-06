Feature: Staff app: consent script, background recording with offline queue, AI note review and sign
  As doctor, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Staff app: consent script, background recording with offline queue, AI
  @todo
  Scenario: Check 1
    Given the doctor declines recording
    When the patient says no
    Then the app opens the manual note and records nothing
  @todo
  Scenario: Check 2
    Given the network drops mid-recording
    When it returns
    Then queued chunks upload in order and none is lost or duplicated
  @todo
  Scenario: Check 3
    Given an AI draft field
    When the doctor has not accepted it
    Then the note cannot be signed
  @todo
  Scenario: Check 4
    Given the phone is lost
    When the queue held audio
    Then it is encrypted and removed on remote revoke

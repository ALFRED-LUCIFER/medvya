Feature: Staff app: sign-in, 15-minute biometric lock, Today, queue, patient search and patient summary
  As doctor, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Staff app: sign-in, 15-minute biometric lock, Today, queue, patient se
  @todo
  Scenario: Check 1
    Given the app is idle for 15 minutes
    When it is opened again
    Then it asks for Face ID, fingerprint or PIN before showing anything
  @todo
  Scenario: Check 2
    Given a receptionist signs in
    When the tabs load
    Then clinical-only tabs are absent and their API calls return 403
  @todo
  Scenario: Check 3
    Given the phone is offline
    When the doctor opens Today
    Then the last copy shows with an offline banner and no edit action
  @todo
  Scenario: Check 4
    Given a patient has an allergy
    When the summary opens
    Then the allergy shows as text and icon, not colour alone

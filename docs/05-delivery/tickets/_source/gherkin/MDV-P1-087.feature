Feature: A free-text medicine needs a reason before authorising when the patient has, or may have, an allergy
  Augmentin 625 Duo, a penicillin-class brand, was typed as free text for a patient with a recorded penicillin allergy and authorised as RX-CAEE099F with No alerts from the checks that could run.

  # chunk: A free-text medicine needs a reason before authorising when the patient has, or may have, an allergy
  @todo
  Scenario: Refused without a reason
    Given Meera Iyer with a recorded penicillin allergy and a free-text line Augmentin 625 Duo
    When the doctor presses Authorise without a reason
    Then the server refuses and the prescription stays a draft

  @todo
  Scenario: The alert names every allergy in words
    Given a patient with two recorded allergies
    When a free-text line is added
    Then the severe alert reads Not checked against, names both allergies and the typed medicine, and does not rely on colour

  @todo
  Scenario: Allergies not recorded also stop it
    Given a patient whose allergies are not recorded
    When a free-text line is added
    Then the same severe alert and reason step apply

  @todo
  Scenario: Reason is kept and printed
    Given the doctor gives a reason and authorises
    When the audit log and the print are read
    Then both carry the reason for that line

  @todo
  Scenario: No known allergies keeps today's path
    Given a patient with No known allergies confirmed
    When a free-text line is added
    Then only the amber Not checked note shows and no reason is asked

  @todo
  Scenario: The rule is on the server and is proven
    Given the integration test on real PostgreSQL that authorises a free-text line for an allergic patient
    When the server rule is removed
    Then the test fails (mutation check, shown in the pull request)

  @todo
  Scenario: Keyboard and screen reader
    Given the alert and the reason field
    When they appear
    Then they are announced, reachable by keyboard and axe finds no violation in English or Arabic

  @todo
  Scenario: No patient data leaks
    Given the refusal
    When logs, telemetry and the address bar are read
    Then they hold no medicine name tied to a patient and no allergy text

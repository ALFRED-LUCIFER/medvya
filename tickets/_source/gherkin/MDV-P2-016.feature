Feature: Allergy and interaction safety checks before authorisation
  As a doctor, I want allergy, interaction and dose checks before authorising, so that unsafe prescriptions are caught.

  # chunk: Add allergy, interaction, duplicate and dose checks
  @todo
  Scenario: Seeded conflicts always alert
    Given a seeded set of allergy classes, interacting pairs, duplicates and out-of-range doses
    When the checks run
    Then the expected alert fires in 100 % of cases (automated test; exit gate 2)
  @todo
  Scenario: Alerts in words with evidence
    Given an alert
    When it is shown
    Then it states severity in words and the evidence, e.g. "Allergy: Penicillin · verified"

  # chunk: Add override reason and source recording
  @todo
  Scenario: Severe alert needs override reason
    Given a severe alert
    When the doctor authorises
    Then an override reason is required, recorded and audited
  @todo
  Scenario: Interaction source recorded
    Given a check result
    When it is stored
    Then the interaction source and version are shown in an info panel and recorded with the result

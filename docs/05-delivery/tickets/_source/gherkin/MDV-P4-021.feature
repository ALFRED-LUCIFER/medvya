Feature: Portal home, released visit summaries (with checked translations), prescriptions, records and labs (R-01)
  As a patient, I want to see what my doctor released, so that I understand my care.

  # chunk: Add released-only portal content
  @todo
  Scenario: Only released items appear
    Given seeded unreleased items, unreviewed AI drafts and internal notes
    When the patient opens the portal
    Then none of them appear
  @todo
  Scenario: PDF download audited
    Given a released visit summary
    When the patient downloads the PDF
    Then it works and is audited

  # chunk: Add checked translations and accessibility
  @todo
  Scenario: Translation disclaimer
    Given a translated summary
    When it is shown
    Then it carries "Translated with AI and checked against the English summary your doctor approved…"
    And only after the doctor approved the source summary
  @todo
  Scenario: Size checks and graceful errors
    Given the portal screens
    When automated checks run
    Then text is at least 17 px and targets at least 48 px
    And errors degrade gracefully when offline

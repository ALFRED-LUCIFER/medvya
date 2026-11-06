Feature: Prescription composer with dose builder and add-medicine-while-prescribing (E-05, E-11)
  As a doctor, I want a prescription composer with a dose builder, so that prescriptions are structured and readable.

  # chunk: Add the dose builder and reconciliation lines
  @todo
  Scenario: Structured and readable sig
    Given a dose, unit, route, frequency, timing, duration and quantity
    When the doctor builds the line
    Then a structured sig and a human-readable instruction in the selected print language are produced
  @todo
  Scenario: Stop lines become discontinue lines
    Given a reconciliation stop decision
    When the composer opens
    Then a discontinue line appears automatically

  # chunk: Add free-text medicines and draft autosave
  @todo
  Scenario: Free text is flagged
    Given a free-text medicine
    When it is added
    Then it is flagged and excluded from interaction checks with a visible warning
  @todo
  Scenario: Draft autosave, not authorised
    Given a draft Rx
    When the doctor edits
    Then it autosaves and is not authorised until MDV-P2-017

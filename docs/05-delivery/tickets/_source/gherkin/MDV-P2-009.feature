Feature: Consultation note editor: chief complaint, SOAP, specialty templates, manual note when recording is declined (E-04)
  As a doctor, I want a SOAP note editor that never loses my work, so that I can document every visit even without AI.

  # chunk: Add SOAP editor with verified prefill
  @todo
  Scenario: Prefill only from verified data
    Given a verified chart
    When the Objective section is prefilled
    Then only verified chart data is used and each item is labelled with its source

  # chunk: Add autosave and recovery
  @todo
  Scenario: Drafts survive failures
    Given a draft note
    When the page refreshes, the network drops or the session times out
    Then the draft is restored, after re-auth with patient confirmation

  # chunk: Add template switching
  @todo
  Scenario: Switch preserves content
    Given typed content in a note
    When the template is switched
    Then content in matching sections is preserved
    And unmapped content triggers a warning

  # chunk: Make the editor accessible
  @todo
  Scenario: Keyboard and zoom
    Given the note editor
    When used by keyboard only and at 200 % zoom
    Then it is fully operable and readable

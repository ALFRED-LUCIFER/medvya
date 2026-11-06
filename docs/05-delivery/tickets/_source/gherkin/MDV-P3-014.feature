Feature: Pre-visit brief: verified chart facts visibly separate from AI summary (A-01)
  As a doctor, I want a pre-visit brief that separates verified facts from AI summary, so that I never mistake AI text for verified data.

  # chunk: Add the split brief with sourced AI bullets
  @todo
  Scenario: Every AI bullet has a source
    Given AI summary bullets
    When shown
    Then each links to a source record and bullets without a source are not shown
  @todo
  Scenario: AI panel visually distinct
    Given the AI panel
    When displayed
    Then it is violet dashed and labelled, never styled as verified

  # chunk: Add fallback and authorisation
  @todo
  Scenario: AI unavailable
    Given the AI service is unavailable
    When the brief opens
    Then verified data only is shown with a notice
  @todo
  Scenario: Authorised and audited
    Given brief generation
    When requested
    Then it respects relationship and purpose authorisation and is audited

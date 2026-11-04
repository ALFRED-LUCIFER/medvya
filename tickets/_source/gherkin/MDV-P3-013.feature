Feature: Template switch and regeneration with confirmation; clinician edits tracked
  As a doctor, I want regeneration to ask first and keep my edits tracked, so that I never lose reviewed work and the new draft stays an AI draft until I accept and sign.

  # chunk: Add confirmed regeneration and edit tracking
  @todo
  Scenario: Regeneration needs confirmation
    Given accepted or edited fields
    When the doctor switches template or regenerates
    Then confirmation is required and nothing is discarded without an explicit choice
  @todo
  Scenario: Draft versions linked to runs
    Given a regeneration
    When it completes
    Then the draft version is kept and linked to its AI run
  @todo
  Scenario: Edit distance computed
    Given clinician edits
    When saved
    Then edit distance per field is computed for the minor-edits metric (MDV-P3-026)

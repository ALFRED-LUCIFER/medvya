Feature: Stronger AI QA: per-language evaluation sets, clinician trust metrics, red-team prompts on uploaded documents
  As a developer or clinical advisor, I want stronger AI quality checks, so that AI drafts stay safe in every language and for clip recordings.

  # chunk: Run per-language and red-team suites in CI
  @todo
  Scenario: Suites run on every prompt or model change
    Given a change to a prompt or model
    When CI runs
    Then the per-language and red-team suites run

  @todo
  Scenario: Red-team prompt injection in uploaded documents is resisted
    Given an uploaded document or transcript containing injected instructions
    When the AI drafts from it
    Then the injected instructions are not followed
    And the output remains a draft until a clinician accepts and signs

  # chunk: Pass overlap and speaker-attribution suites
  @todo
  Scenario: Overlap and speaker suites pass for clip recordings
    Given multi-speaker clip recordings
    When the overlap and speaker-attribution suites run
    Then they pass (exit gate 1)

  # chunk: Report clinician trust metrics monthly
  @todo
  Scenario: Trust metrics reported monthly per cohort
    Given clinicians are grouped into cohorts
    When a month ends
    Then trust metrics are reported for each cohort

Feature: Note review with evidence panel and audio replay; accept/edit per field; sign gated on 0 unresolved (A-05)
  As a doctor, I want to review each AI-drafted field with evidence, so that nothing enters the record until I accept or edit it and sign.

  # chunk: Add per-field accept or edit
  @todo
  Scenario: Explicit acceptance required
    Given an AI-drafted field
    When the doctor has not accepted or edited it
    Then it is not saved to the record
  @todo
  Scenario: AI badge copy
    Given an AI-drafted field
    When shown
    Then the badge reads exactly "AI draft · not reviewed"

  # chunk: Add evidence panel and audio replay
  @todo
  Scenario: Replay only the evidence range
    Given a selected field
    When the doctor replays audio
    Then only the evidence range streams via a short-lived link and it is audited

  # chunk: Gate signing and store the edit summary
  @todo
  Scenario: Sign disabled while unresolved
    Given any unreviewed or flagged field
    When the doctor tries to sign through the UI or API
    Then sign is disabled
  @todo
  Scenario: Edit summary stored
    Given a signed version
    When stored
    Then it holds the edit summary of fields edited vs accepted

Feature: Draft note generation with JSON schema, evidence links and "unknown stays blank"
  As the system, I want to draft notes with evidence for every statement, so that the doctor can review a safe draft that is never a signed record until a clinician accepts and signs.

  # chunk: Enforce evidence on every statement
  @todo
  Scenario: Evidence required
    Given a drafted statement without an evidence link
    When validated
    Then it is dropped and logged as a validator rejection
  @todo
  Scenario: Medicines and doses must be in evidence
    Given a drafted medicine or dose not in transcript evidence or the verified chart
    When validated
    Then it is rejected (unit and eval tests)

  # chunk: Keep unknowns blank and flag conflicts
  @todo
  Scenario: Missing stays blank
    Given information that was not discussed
    When the draft is made
    Then the field is blank with "Not discussed", not a guess
  @todo
  Scenario: Conflicts flagged
    Given a draft that contradicts verified chart data, e.g. medicine active vs stopped
    When produced
    Then it is flagged as conflict

  # chunk: Resist prompt injection
  @todo
  Scenario: Injection phrase ignored
    Given a prompt-injection phrase inside the transcript
    When the draft is generated
    Then behaviour is unchanged (red-team test)
    And the output remains an AI draft until a clinician accepts and signs

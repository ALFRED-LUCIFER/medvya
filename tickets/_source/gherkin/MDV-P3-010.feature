Feature: Overlapping speech and unconfirmed speaker handling (A-10)
  As a doctor, I want overlapping speech flagged until resolved, so that uncertain attribution never reaches the note unmarked.

  # chunk: Flag and resolve overlaps
  @todo
  Scenario: Uncertain segments cannot back unflagged statements
    Given a segment flagged as overlap or uncertain
    When a draft statement is derived from it
    Then the statement stays flagged
  @todo
  Scenario: Resolve updates dependent fields
    Given an overlap
    When the doctor chooses Assign speaker or Mark unknown
    Then all dependent draft fields' flags update
  @todo
  Scenario: Sign blocked while unresolved
    Given unresolved overlap flags
    When the doctor tries to sign
    Then sign is blocked and they count in "n unresolved"

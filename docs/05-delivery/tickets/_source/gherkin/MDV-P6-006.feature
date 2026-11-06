Feature: Private voice assistant through earpiece only (A-07)
  As a doctor, I want to ask the chart a question and hear the answer only in my earpiece, so that patient information stays private in the room.

  # chunk: Answer from the verified chart with its source
  @todo
  Scenario: Answer cites its chart source
    Given a doctor with a patient's verified chart open
    When the doctor asks "last eGFR" by tap or hotword
    Then the answer is shown as text first
    And it cites its chart source

  @todo
  Scenario: No diagnosis or treatment advice
    Given the assistant is on
    When the doctor asks for a diagnosis or treatment advice
    Then the assistant declines and gives none

  # chunk: Speak only through a private earpiece
  @todo
  Scenario: Assistant never speaks through the room speaker
    Given no private earpiece is connected
    When the doctor asks a question
    Then nothing is spoken through the room speaker

  @todo
  Scenario: Disconnecting the earpiece disables the assistant immediately
    Given the assistant is active with a connected earpiece
    When the earpiece disconnects
    Then the assistant turns off immediately

  # chunk: Audit queries and answers
  @todo
  Scenario: Queries and answers are audited without storing audio beyond policy
    Given a doctor asked a question and heard the answer
    When the audit log is reviewed
    Then the query and answer are recorded
    And no audio is stored beyond the retention policy

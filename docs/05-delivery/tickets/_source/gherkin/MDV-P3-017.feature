Feature: Wrong-patient suspicion locks the session; re-binding needs a reason and is audited (A-09)
  As a doctor, I want a suspected wrong-patient recording to lock, so that audio never lands on the wrong chart.

  # chunk: Add stop and lock with audited re-binding
  @todo
  Scenario: Stop and lock freezes session
    Given a different name is heard or a mix-up is reported
    When the doctor chooses Stop & lock
    Then further capture and drafting for that session are prevented
  @todo
  Scenario: Re-binding needs reason and new consent
    Given a locked session
    When re-bound to another patient
    Then a reason and new consent for the correct encounter are required and both are audited
  @todo
  Scenario: One active encounter per recording
    Given a recording
    When attached to a second active encounter
    Then it is refused (constraint test)

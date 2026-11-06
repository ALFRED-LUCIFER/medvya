Feature: AI unavailable → editable manual note with no data loss (A-06)
  As a doctor, I want a manual note when AI is down, so that I can keep working without losing anything.

  # chunk: Add the AI outage state and manual fallback
  @todo
  Scenario: Outage shown without loss
    Given a simulated AI outage
    When the timeout passes
    Then "AI drafting is unavailable right now. Your recording is saved." is shown and no audio or typed text is lost
  @todo
  Scenario: Notify me later
    Given the doctor chooses "Notify me later"
    When the draft becomes available
    Then a notification is created
  @todo
  Scenario: Late draft never overwrites
    Given manual content exists
    When a later AI draft arrives
    Then it is offered as a separate draft to compare, still an AI draft until accepted and signed

Feature: Queue in the consultation: tokens and ages only, escalations first, and a Next button on tablets
  The doctor cannot see who is next, or that a nurse escalated someone, without leaving the note.

  # chunk: Queue in the consultation: tokens and ages only, escalations first, and a Next button on tablets
  @todo
  Scenario: Tokens and ages only
    Given a queue of five patients
    When the rail renders
    Then each row shows a token and an age band and the page holds no other patient's name, complaint or escalation reason

  @todo
  Scenario: The privacy rule is proven
    Given the rail's test
    When a name is added to a row
    Then the test fails (mutation check)

  @todo
  Scenario: Escalations first
    Given an escalated visit
    When the rail renders
    Then it is the first row and carries the word Escalated

  @todo
  Scenario: Same order as My day
    Given the clinic's queue rule
    When the rail and My day are compared
    Then the order is the same

  @todo
  Scenario: Switching is deliberate
    Given an open consultation
    When another token is chosen
    Then the another consultation is open challenge appears and nothing changes until the doctor answers

  @todo
  Scenario: Tablet button, languages and axe
    Given 1024 px
    When the strip renders
    Then the rail is the button Next with the token and the waiting count; and axe finds no violation in English or Arabic

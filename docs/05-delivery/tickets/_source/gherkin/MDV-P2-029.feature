Feature: Encounters start race: a second start that loses the race answers with the winner's encounter
  As a doctor, I want a double start of a consultation to open the same consultation, so that a double click or a second tab never shows an error.

  # chunk: Make the losing start answer with the winner
  @todo
  Scenario: The loser read no encounter before the winner committed
    Given a checked-in visit and two starts by the same doctor
    When the second start has read no encounter for the visit and the first then commits
    Then the second start answers with the first start's encounter
    And it is not refused as another open consultation
  @todo
  Scenario: The loser's insert meets the unique index
    Given a checked-in visit and two starts by the same doctor
    When both pass their checks and the first commits before the second inserts
    Then the second start answers with the first start's encounter
  @todo
  Scenario: The visit moves once
    Given two starts of the same visit that race
    When both have answered
    Then there is one encounter, the visit is In consult and the board was told once

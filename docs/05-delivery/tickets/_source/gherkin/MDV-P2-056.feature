Feature: Truthful status and small motion: an Updated mark on live board changes, skeletons, an offline strip, named transition properties
  A card changed by someone else arrives with no cue.

  # chunk: Truthful status and small motion: an Updated mark on live board changes, skeletons, an offline strip, named transition properties
  @todo
  Scenario: Someone else's change is marked in words
    Given two people on one board
    When one moves a card
    Then the other sees the tag Updated on that card for 5 seconds and a screen reader announces it once

  @todo
  Scenario: No movement
    Given the Updated mark and the skeletons
    When they play
    Then no element changes position, and under reduced motion there is no fade

  @todo
  Scenario: No jump on load
    Given the board on a slow connection
    When data arrives
    Then the layout height changes by less than 10 % from the skeleton

  @todo
  Scenario: Offline says what is true for the screen
    Given the board offline
    When the strip shows
    Then it names the last update time, covers no column and does not mention recordings

  @todo
  Scenario: Focus ring at once
    Given a button
    When it is focused by keyboard
    Then the ring is at full colour on the first frame, and no library component uses transition-all

  @todo
  Scenario: Nothing from the never list
    Given the change
    When it is reviewed
    Then no page transition, sliding card, staggered list, animated counter or toast was added

  @todo
  Scenario: Languages and axe
    Given English and Arabic
    When axe runs with skeletons and the strip showing
    Then there is no violation

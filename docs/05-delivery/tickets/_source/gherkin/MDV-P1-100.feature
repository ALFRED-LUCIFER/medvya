Feature: Front desk: undo the last move, Left without being seen, cancel a checked-in visit; the desk cannot close a visit under the doctor
  Call in and Consult done are single taps with no confirmation and no undo.

  # chunk: Undo the last move, enforced on the server
  @todo
  Scenario: Undo restores the visit
    Given a card moved by Call in
    When Undo is pressed within 30 seconds
    Then the card is back in Ready for doctor with the same token

  @todo
  Scenario: The window is enforced on the server
    Given a move older than the window or followed by another move
    When an undo is sent
    Then the server refuses it

  @todo
  Scenario: Left without being seen is its own state
    Given a checked-in walk-in
    When the desk marks Left without being seen with a reason
    Then the token leaves the queue display and the visit is not counted as done

  # chunk: Left without being seen and Cancel visit
  @todo
  Scenario: The desk cannot close under the doctor
    Given an open consultation for the visit
    When the desk views the card
    Then it reads With the doctor with no action, and a direct API call to close it is refused

  @todo
  Scenario: Everyone follows
    Given an undo or a new end state
    When the queue display and Today are watched
    Then both follow within one live update

  # chunk: The desk cannot close a visit under the doctor; audit and checks
  @todo
  Scenario: Reversals are audited
    Given any undo, cancel or left-without-being-seen
    When the audit log is read
    Then it holds who, when, the reason and the previous state

  @todo
  Scenario: Server rules proven
    Given the integration tests
    When the window rule or the open-consultation guard is removed
    Then a test fails

  @todo
  Scenario: Walk-in not longer; keyboard and axe
    Given the walk-in path
    When it is measured
    Then it is still at most 6 clicks new and 3 returning; and the card menu works by keyboard with no axe violation in English or Arabic

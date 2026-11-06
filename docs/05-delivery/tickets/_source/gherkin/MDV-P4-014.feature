Feature: Results intake and inbound report identity match (manual upload/entry; low-confidence needs a human) (C-05)
  As a care coordinator, I want inbound results matched to the right patient by a human when unsure, so that results never reach the wrong chart.

  # chunk: Add identity matching with human confirmation
  @todo
  Scenario: Low confidence blocked
    Given a result with a low-confidence match
    When no user has confirmed
    Then it cannot enter any patient chart in the UI or API
  @todo
  Scenario: Candidates and confirmation record
    Given match candidates
    When the user reviews them
    Then each shows MRN, DOB and open orders
    And the confirmation records actor and time

  # chunk: Add order linking and unmatched queue
  @todo
  Scenario: Match moves order state
    Given a result matched to an open order
    When it is confirmed
    Then the order's loop state moves to Result received
  @todo
  Scenario: Unmatched queue
    Given unmatched documents
    When the queue is viewed
    Then they stay in an Unmatched queue with a count

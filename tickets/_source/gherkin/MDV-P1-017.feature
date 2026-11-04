Feature: Duplicate / merge review with second approver when both records hold clinical data (P-03)
  As a practice admin, I want a controlled merge review, so that duplicate patients are merged safely and reversibly.

  # chunk: Add not-the-same-person decision
  @todo
  Scenario: "Not the same person" suppresses the pair
    When a reviewer chooses "Not the same person"
    Then the request closes
    And the same pair is suppressed from future suggestions

  # chunk: Enforce merge approvals
  @todo
  Scenario: Merge needs required approvers
    When a merge is attempted without the required approver(s)
    Then the server refuses it
    And approvals are audited

  # chunk: Execute reversible merge with history
  @todo
  Scenario: After merge either MRN finds the survivor
    Given a merge is approved
    When it executes
    Then searching either MRN finds the surviving record
    And appointments and identifiers move

  @todo
  Scenario: Merge can be undone by an admin
    Given a completed merge within the retention period
    When an admin undoes it
    Then the original records are restored

  @todo
  Scenario: Merge history is visible on the patient record
    When a user opens the surviving patient record
    Then the merge history is visible

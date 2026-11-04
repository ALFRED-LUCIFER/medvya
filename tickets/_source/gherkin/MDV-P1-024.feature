Feature: Reschedule (incl. drag with confirm), cancel with reason, mark no-show with rebook offer
  As a receptionist, I want to reschedule, cancel or mark no-show with clear rules, so that the calendar stays accurate.

  # chunk: Add appointment actions with state rules
  @done
  Scenario: Disallowed actions are blocked
    Given an appointment in a state that does not allow an action
    Then the action is disabled with an explanation
    And the API rejects it if called

  @done
  Scenario: Cancel needs a reason
    When a receptionist cancels without a reason
    Then it is impossible
    And reasons are a short list plus free text

  @done
  Scenario: Reschedule keeps history and frees the old slot
    When an appointment is rescheduled
    Then history keeps old slot, new slot, actor and time
    And the old slot is freed atomically

  @done
  Scenario: No-show offers rebook
    When an appointment is marked no-show
    Then a rebook is offered with the patient preselected

  # chunk: Count no-shows in the metric
  @todo
  Scenario: No-show is counted in the no-show metric
    When an appointment is marked no-show
    Then it is counted in the no-show metric (MDV-P1-041)

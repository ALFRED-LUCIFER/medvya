Feature: Split and partial payments with advance usage; balance moves to dues (P-10)
  As a receptionist, I want to collect one invoice across several methods, so that partial payments and advances are handled.

  # chunk: Add split payments with live balance
  @todo
  Scenario: Over-collection blocked
    Given an invoice being collected
    When the splits exceed the amount
    Then confirmation is not possible
  @todo
  Scenario: Splits have own state
    Given cash, UPI, card and advance splits
    When non-cash splits are confirmed
    Then each split is a PAYMENT_SPLIT with its own state
    And the invoice is Paid only when all non-cash splits are confirmed
  @todo
  Scenario: Remaining balance becomes due
    Given a confirmed collection with a remaining balance
    When it is confirmed
    Then a DUE with a due date is created and appears in dues (P-12)
  @todo
  Scenario: Advance usage atomic
    Given a patient advance balance
    When it is used in a split
    Then the advance balance is reduced atomically
  @todo
  Scenario: India no further rounding
    Given an India invoice with a round-off line
    When payment is collected
    Then the amount collected is the rounded payable total
    And payments never round again
  @todo
  Scenario Outline: UAE cash rounding down to 25 fils
    Given a UAE cash split
    When the amount is <amount>
    Then <collected> is collected and rounding of <rounding> is recorded as a rounding line
    Examples:
      | amount | collected | rounding |
      | AED 10.40 | AED 10.25 | AED 0.15 |
      | AED 10.25 | AED 10.25 | none |
  @todo
  Scenario: UAE card and online exact
    Given a UAE card or online payment
    When it is collected
    Then it is exact to the fils

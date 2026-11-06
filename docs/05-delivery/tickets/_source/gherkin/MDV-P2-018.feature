Feature: Investigation orders composer with Draft → Ordered states (E-05)
  As a doctor, I want to compose and place investigation orders separately from the note, so that tests are ordered deliberately.

  # chunk: Add the orders composer, states and slip
  @todo
  Scenario: Draft until Place orders
    Given draft orders
    When the doctor places them
    Then they become Ordered, shown with icon and label, and placing is audited
  @todo
  Scenario: Investigation slip PDF
    Given placed orders
    When the slip is printed or shared
    Then it carries patient identifiers and instructions
  @todo
  Scenario: Orders on the timeline
    Given an order in any state
    When the timeline is viewed
    Then it appears with its state
  @todo
  Scenario: No automatic ordering on signing
    Given draft orders
    When the note is signed
    Then no order is placed automatically

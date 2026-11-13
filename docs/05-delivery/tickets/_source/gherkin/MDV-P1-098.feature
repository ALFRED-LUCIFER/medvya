Feature: Phone rule: under 768 px clinical writes say Open this on a tablet or computer
  At 375 px the prescription table scrolls sideways inside itself and hides dose columns; the consultation is 4,941 px tall.

  # chunk: Phone rule: under 768 px clinical writes say Open this on a tablet or computer
  @todo
  Scenario: Writes are replaced by one message
    Given a doctor at 375 px on an open consultation
    When the prescription, orders, reconciliation or Review and sign is reached
    Then each shows Open this on a tablet or computer with the patient's name and no write control

  @todo
  Scenario: Reading still works
    Given the same phone
    When My day, the queue, the chart and an escalation are opened
    Then each is readable with the allergy first and no sideways scrolling

  @todo
  Scenario: Addendum allowed
    Given a signed note at 375 px
    When the doctor adds an addendum
    Then it can be written and signed

  @todo
  Scenario: Tablet is first-class
    Given 768 px and wider
    When the same screens open
    Then every write control is present

  @todo
  Scenario: Not a security control
    Given the API
    When a write is sent from any device by an authorised user
    Then the server's answer is the same as before this change

  @todo
  Scenario: Languages and axe
    Given English and Arabic at 375 px
    When axe runs
    Then there is no violation

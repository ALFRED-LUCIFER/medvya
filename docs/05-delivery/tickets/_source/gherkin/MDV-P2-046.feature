Feature: Token slip: a small printable slip and Reprint; the display names the doctor per token after a reassignment
  Tokens cannot be printed, reissued or cancelled; the desk speaks or writes the number by hand.

  # chunk: Token slip: a small printable slip and Reprint; the display names the doctor per token after a reassignment
  @todo
  Scenario: Slip holds no patient data
    Given a checked-in visit
    When the slip is printed
    Then it shows clinic, token, doctor, date and time and no name, MRN, phone or reason, and the test fails if a name is added

  @todo
  Scenario: Reprint works
    Given a waiting visit
    When Reprint is chosen from the card menu
    Then the same token prints

  @todo
  Scenario: Void tokens do not print
    Given a cancelled visit
    When Reprint is tried
    Then it is not offered and the server refuses

  @todo
  Scenario: The display names the doctor
    Given a reassigned token
    When the queue display renders
    Then the doctor's name is beside the token and no patient name appears

  @todo
  Scenario: Walk-in not longer
    Given the walk-in path
    When it is measured without printing
    Then it is still at most 6 clicks new and 3 returning

  @todo
  Scenario: Print view is accessible
    Given the slip view
    When axe runs in English and Arabic
    Then there is no violation and it has one h1

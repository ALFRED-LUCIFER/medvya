Feature: Reception training (30 min), one-page desk card and support channel with named contact
  As a founder, I want receptionists trained and supported, so that they can run the desk confidently.

  # chunk: Deliver training, desk card and support contact
  @todo
  Scenario: Training delivered and recorded
    Then training is delivered to every receptionist at both clinics
    And attendance is recorded

  @todo
  Scenario: Desk card at every desk
    Then a desk card is printed and available at each desk
    And it covers the top 8 tasks and who to call

  @todo
  Scenario: Support details are communicated
    Then support contact, hours and escalation are communicated in writing
    And listed in the R1 release notes

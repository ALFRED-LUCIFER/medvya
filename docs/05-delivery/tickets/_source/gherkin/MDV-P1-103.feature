Feature: One queue rule for the clinic: the doctor's day follows it, shows tokens and puts nurse escalations first
  A 72-year-old escalated for fever and BP 168/96 was row 11 of 11 on the doctor's list, below the first screen, because rows sort by slot time and a walk-in holds a late nominal slot.

  # chunk: One queue rule for the clinic: the doctor's day follows it, shows tokens and puts nurse escalations first
  @todo
  Scenario: Escalated first
    Given a nurse escalation
    When the doctor opens the day at 1024 by 768
    Then the escalated visit is in the first viewport above Next patient with its reason in words

  @todo
  Scenario: Three screens, one order
    Given the clinic's rule
    When the display's Next list, the board column and the doctor's day are compared
    Then the order is the same

  @todo
  Scenario: Tokens on every row
    Given arrived patients
    When the doctor's day renders
    Then each row shows its token

  @todo
  Scenario: Walk-ins by arrival
    Given a walk-in who arrived at 09:58 with a nominal 19:00 slot
    When the arrival rule is set
    Then she sorts by 09:58

  @todo
  Scenario: The rule is on the server and proven
    Given the ordered-queue query
    When the escalation-first rule is removed
    Then the integration test fails; a change of the setting is audited and scoped to the tenant

  @todo
  Scenario: Display stays private
    Given the queue display
    When it is read
    Then it shows tokens and the doctor per token and no name

  @todo
  Scenario: Languages and axe
    Given English and Arabic
    When axe runs on the doctor's day
    Then there is no violation and escalation is not conveyed by colour alone

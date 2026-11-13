Feature: The signing checklist reports only what happened: no Identity confirmed tick the doctor did not make
  On opening a consultation the checklist already says Identity confirmed (name and date of birth) · Done; in code the item only means the banner was displayed.

  # chunk: The signing checklist reports only what happened: no Identity confirmed tick the doctor did not make
  @todo
  Scenario: Nothing is ticked on opening
    Given a newly opened consultation
    When the checklist is read
    Then no item about identity is marked done

  @todo
  Scenario: A tick needs an act
    Given the tap option
    When the doctor presses Confirmed with the patient
    Then the item is done and the record holds who and when; given the wording option, then the item reads Patient shown and has no tick

  @todo
  Scenario: No AI line without AI
    Given a clinic with no AI drafting
    When the checklist renders
    Then the AI line is absent

  @todo
  Scenario: Signing still gated
    Given unreviewed items
    When the doctor tries to sign
    Then signing stays locked as today

  @todo
  Scenario: The rule is proven
    Given the unit test for the checklist
    When the item is derived from the banner being displayed again
    Then the test fails

  @todo
  Scenario: Languages and axe
    Given English and Arabic
    When axe runs on the checklist
    Then there is no violation and done is conveyed in words

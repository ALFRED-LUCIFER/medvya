Feature: Staff: only an owner can offboard an owner, step-up on any offboarding, change a role in place; link or remove /admin/staff
  A practice admin is offered Offboard on the owner doctor and the dialog opens; the code refuses only the last administrator.

  # chunk: Staff: only an owner can offboard an owner, step-up on any offboarding, change a role in place; link or remove /admin/staff
  @todo
  Scenario: Today's behaviour is recorded
    Given a practice admin and an owner
    When the admin calls the offboard endpoint for the owner on main
    Then a test records the answer and the issue states it

  @todo
  Scenario: Only an owner offboards an owner
    Given a practice admin
    When the offboard endpoint is called for an owner
    Then the server answers 403 and nothing changes

  @todo
  Scenario: Never the last owner
    Given one owner
    When anyone tries to offboard them
    Then the server refuses

  @todo
  Scenario: Step-up on every offboarding
    Given any offboarding
    When it is confirmed without a recent step-up
    Then the server asks for it

  @todo
  Scenario: Change role works and is audited
    Given a receptionist
    When an owner changes her to nurse
    Then her next request has the nurse's access, her old sessions end, and the audit log holds who, when, from and to

  @todo
  Scenario: The rules are proven
    Given the integration tests
    When the owner rule or the step-up requirement is removed
    Then a test fails

  @todo
  Scenario: The screen matches the server
    Given the staff table
    When a user may not offboard a row
    Then no Offboard button is offered there; axe finds no violation in English or Arabic

  @todo
  Scenario: One invite flow
    Given the app
    When /admin/staff is opened
    Then it is linked from Settings or answers Page not found

Feature: Staff offboarding removes access immediately and ends all sessions
  As a practice admin, I want offboarding to remove access at once, so that former staff cannot reach patient data.

  # chunk: Add staff offboarding with session revocation
  @done
  Scenario: Sessions fail within 60 seconds after offboarding
    Given a staff member has active sessions in the tenant
    When they are offboarded
    Then their next request from any session in that tenant fails within 60 seconds
    And they cannot sign in to that tenant

  @done
  Scenario: Other tenants are unaffected
    Given the person also has memberships in other tenants
    When they are offboarded from one tenant
    Then the other memberships are unaffected

  @done
  Scenario: Offboarding needs confirmation, reason and audit
    When an admin offboards a staff member
    Then confirmation and a reason are required
    And the action is audited

  @done
  Scenario: Last practice admin cannot offboard themselves
    Given a user is the last practice admin
    When they try to offboard themselves
    Then the action is refused

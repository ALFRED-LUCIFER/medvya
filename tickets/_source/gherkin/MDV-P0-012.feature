Feature: Session control: facility picker, idle timeout on shared clinic PCs, device/session revocation
  As a staff member, I want sessions scoped to my facility and ended safely, so that shared clinic PCs do not expose patient data.

  # chunk: Add facility picker and permission-safe context
  @done
  Scenario: User with two memberships must choose a facility
    Given a user with two memberships
    When they sign in
    Then they must choose a facility
    And the session is scoped to it

  @done
  Scenario: Switching facility needs no new MFA
    When the user switches facility
    Then the session is re-scoped without re-entering MFA

  @done
  Scenario: Context never grants more than the role
    When /v1/me/context is requested
    Then it never returns permissions the role does not grant

  # chunk: End idle sessions and revoke sessions on request
  @done
  Scenario: Idle timeout ends the session
    When the tenant's idle timeout passes
    Then the session ends
    And the "Session expired" screen (F-05a) appears

  @done
  Scenario: Revoked session ends on the next request
    When a user from "My devices" or an admin revokes a session
    Then it ends within 60 seconds on the next request

  # chunk: Resume after re-sign-in and keep patient data out of browser storage
  @todo
  Scenario: Re-sign-in returns to the previous route and confirms the patient
    Given a session ended by idle timeout
    When the user signs in again
    Then they return to the previous route
    And they are asked to confirm the patient before continuing

  @todo
  Scenario: No clear-text patient data in browser storage
    When a Playwright check inspects localStorage and sessionStorage
    Then no patient data is stored in clear text

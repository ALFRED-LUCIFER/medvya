Feature: Loss/theft revoke effective in under one minute (W-03)
  As a doctor or practice admin, I want to revoke a lost or stolen clip quickly, so that nobody can use it or read its queued audio.

  # chunk: Revoke a clip with a reason and audit it
  @todo
  Scenario Outline: Revoke with a reason
    Given a paired clip with queued audio
    When the admin revokes it with reason "<reason>"
    Then the binding is revoked and keys and tokens are invalidated
    And queued audio on the device becomes unreadable
    And an audit event is written and the admin is notified

    Examples:
      | reason  |
      | lost    |
      | stolen  |
      | retired |

  @todo
  Scenario: Revoked device cannot authenticate or upload within 60 seconds
    Given a clip was revoked
    When 60 seconds have passed
    Then the device cannot authenticate or upload (exit gate 3 test)

  @todo
  Scenario: Revoked device must be re-provisioned
    Given a revoked clip
    When someone tries to use it again without re-provisioning
    Then it cannot be used

Feature: Platform console shell (`web/admin`): company sign-on plus security key, platform roles, reason on every action, no PHI
  As medvya operations, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Platform console shell (`web/admin`): company sign-on plus security ke
  @todo
  Scenario: Check 1
    Given a Medvya user
    When signs in without a security key
    Then access is refused
  @todo
  Scenario: Check 2
    Given a platform action
    When is taken without a reason
    Then it is rejected
  @todo
  Scenario: Check 3
    Given any console endpoint
    When returns data
    Then it holds no patient field (architecture test)
  @todo
  Scenario: Check 4
    Given a key is lost
    When another owner verifies on a video call
    Then a one-hour link is issued, the old key is revoked and both steps are logged

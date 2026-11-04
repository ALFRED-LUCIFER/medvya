Feature: Packages and advances with session redemption (P-11)
  As a receptionist, I want to sell packages and redeem sessions, so that prepaid care is tracked.

  # chunk: Add package sale and session redemption
  @todo
  Scenario: Atomic redemption
    Given a package with sessions left
    When a session is redeemed
    Then sessions decrement atomically
    And redemption cannot exceed the total
  @todo
  Scenario: Expiry status and listing
    Given packages with validity dates
    When the day rolls over
    Then expiry status updates daily
    And expiring-soon packages are listed for reception
  @todo
  Scenario: Unused balance value
    Given an expired package
    When the refund policy from settings is applied
    Then the unused balance value is computed and shown

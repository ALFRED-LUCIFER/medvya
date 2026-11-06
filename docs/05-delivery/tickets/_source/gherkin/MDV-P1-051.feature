Feature: Encrypted read-only schedule cache for outages (needs an ADR first)
  As a receptionist, I want to read today's schedule during a long outage, so that I can still call patients in.

  # chunk: Record the decision
  @todo
  Scenario: An ADR is accepted first
    Then an ADR records what is stored in the browser, the key handling and the reasons
    And it is accepted before implementation

  # chunk: Cache the schedule encrypted
  @todo
  Scenario: Schedule readable during outage
    Given the receptionist loaded today's schedule while online
    When the network drops
    Then the schedule is shown read-only with an "as of <time>" label
    And create, edit and cancel are disabled

  @todo
  Scenario: Cache is wiped
    When the user signs out, or 12 hours pass, or the account is deactivated
    Then the cached schedule is deleted and unreadable

  @todo
  Scenario: Stored data is encrypted (mutation check)
    Then inspecting the browser store shows only ciphertext
    And the test fails if encryption is removed

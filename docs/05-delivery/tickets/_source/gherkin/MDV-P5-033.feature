Feature: Mobile backend: native token sign-in, device registry, push registry and remote revoke
  As developer, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Mobile backend: native token sign-in, device registry, push registry a
  @todo
  Scenario: Check 1
    Given a staff member is offboarded
    When the owner confirms
    Then every mobile session and push token of that person is revoked at once
  @todo
  Scenario: Check 2
    Given a refresh token is replayed
    When the server sees it twice
    Then the whole token family is revoked and the event is audited
  @todo
  Scenario: Check 3
    Given a wrong SMS code is entered five times
    When the limit is reached
    Then the number is locked for a period and the patient is told in plain words
  @todo
  Scenario: Check 4
    Given a lost phone is reported
    When the user revokes it in My devices
    Then the next request from that device is refused

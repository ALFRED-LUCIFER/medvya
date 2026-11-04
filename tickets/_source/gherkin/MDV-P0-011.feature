Feature: Staff sign-in through the BFF with Entra External ID: MFA (TOTP), passkeys and recovery
  As a staff member, I want to sign in securely with MFA, so that only verified staff reach patient data.

  # chunk: Require MFA and keep tokens server-side
  @done
  Scenario: MFA is required before anything authenticated
    Given a staff user who has not completed MFA
    When they try to reach any authenticated page or API
    Then access is refused

  @done
  Scenario: No token is readable from JavaScript
    Given a staff user signed in
    Then no access or refresh token is readable from JavaScript
    And document.cookie and storage contain none

  # chunk: Add wrong-code lockout
  @todo
  Scenario: Wrong code warns then locks the account
    When a user enters a wrong code
    Then they see "Incorrect code. 2 attempts left before a 15-minute lock."
    And after the final failure the account is locked for 15 minutes
    And the limit is enforced server-side

  # chunk: Add passkey fallback and SSO-enforced tenant sign-in
  @todo
  Scenario: Browser without passkey support falls back to one-time code
    Given a browser that does not support passkeys
    When the user signs in
    Then the one-time code path is used
    And a clear message explains why

  @todo
  Scenario: SSO-enforced tenant hides password and OTP options
    Given a tenant with the SSO-enforced flag set
    When the sign-in screen is shown
    Then password and OTP options are hidden
    And "Hospital staff? Sign in with SSO" is shown

  # chunk: Add account recovery and audit of failed sign-in events
  @todo
  Scenario: Recovery re-verifies identity and forces MFA re-enrolment
    When a user chooses "Can't sign in?"
    Then identity is re-verified
    And MFA re-enrolment is forced

  @todo
  Scenario: Recovery never reveals whether an account exists
    When recovery is requested for an unknown account
    Then the response is the same as for a known account

  @todo
  Scenario: Sign-in, failure, lockout and recovery events are audited
    When any of sign-in, failure, lockout or recovery happens
    Then an event is written to the audit log

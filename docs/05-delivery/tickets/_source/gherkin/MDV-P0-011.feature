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
    When a user enters wrong codes on the provider's page
    Then the provider counts them and locks the account for 15 minutes after the final failure
    And Medvya never sees the code
    And starting sign-in is rate-limited by Medvya

  # chunk: Add passkey fallback and SSO-enforced tenant sign-in
  @todo
  Scenario: Browser without passkey support falls back to one-time code
    Given a browser that does not support passkeys
    When the user signs in
    Then the one-time code path is used
    And a clear message explains why

  # SSO-enforced tenant sign-in moved to MDV-P7-004

  # chunk: Add account recovery and audit of failed sign-in events
  @todo
  Scenario: Recovery re-verifies identity and forces MFA re-enrolment
    When a user chooses "Can't sign in?"
    Then the provider's recovery re-verifies identity
    And MFA re-enrolment is forced

  @todo
  Scenario: Recovery never reveals whether an account exists
    When sign-in is started without an email
    Then Medvya's page reveals nothing about any account

  @todo
  Scenario: Sign-in, failure, lockout and recovery events are audited
    When sign-in, a refusal or a sign-out reaches Medvya
    Then an event is written to the audit log
    And provider-side failures are in the Entra sign-in log, per the runbook

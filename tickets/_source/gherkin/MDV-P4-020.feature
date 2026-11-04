Feature: Patient portal authentication: OTP sign-in, portal invitations, patient session (BFF)
  As a patient, I want to sign in to the portal with an OTP, so that I can safely see my own records.

  # chunk: Add OTP sign-in and session
  @todo
  Scenario: OTP sign-in with rate limit
    Given a patient with a portal invitation
    When they sign in with an OTP
    Then they get a session
    And brute force is rate-limited and locked per policy
  @todo
  Scenario: Session expiry and revocation
    Given a patient session
    When it expires or is revoked
    Then access ends
    And no tokens are in browser storage

  # chunk: Add record isolation and performance check
  @todo
  Scenario: Own records only
    Given a signed-in patient
    When portal endpoints are called for others' data
    Then only their own records and proxies they hold are visible
    And IDOR tests pass on all portal endpoints
  @todo
  Scenario: Portal p95 under 2 s
    Given the 4G mobile profile
    When home and records are measured
    Then p95 is under 2 s

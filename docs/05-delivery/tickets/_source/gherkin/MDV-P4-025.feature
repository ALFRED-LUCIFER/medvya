Feature: Caregiver proxy authorisation with scopes, expiry, OTP verification and revocation (R-04)
  As a patient, I want to authorise a caregiver for limited scopes, so that they can help me safely.

  # chunk: Add caregiver scopes, expiry and revocation
  @todo
  Scenario: Scope and expiry enforced
    Given a caregiver with granted scopes
    When they access data
    Then only granted scopes are available until expiry
    And authorisation tests pass per scope
  @todo
  Scenario: Revocation within 1 minute
    Given a revoked caregiver
    When 1 minute has passed
    Then access has ended

  # chunk: Add caregiver banner, audit and minor rules
  @todo
  Scenario: Banner and audit
    Given a caregiver session
    When they act
    Then the banner is always visible
    And actions are audited as the caregiver acting for the patient
  @todo
  Scenario: Minor and guardian rules
    Given minor or guardian proxies
    When configured
    Then rules are set per country after legal confirmation

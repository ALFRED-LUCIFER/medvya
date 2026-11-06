Feature: Clinical authorisation: care relationship and purpose checks, request access for clinics
  As a clinician or practice owner, I want care-relationship and purpose checks on clinical reads, so that access is least-privilege and audited.

  # chunk: Add care relationship check and request access
  @todo
  Scenario: No relationship means 403 and request
    Given a clinician without a care relationship
    When they open a patient
    Then they get 403 and "Request access", audited
    And owners can grant time-limited access

  # chunk: Add purpose recording and clinic policy
  @todo
  Scenario: Purpose on every clinical read
    Given a clinical read
    When it happens
    Then its purpose (treatment, registration, billing, audit) is recorded
  @todo
  Scenario: Clinic policy configurable
    Given the policy "all facility clinicians" or "assigned only"
    When configured
    Then each behaves as set and is tested
  @todo
  Scenario: Nurse access limited by matrix
    Given a nurse
    When using Patient 360 and entry screens
    Then access is Limited on Patient 360 with entry of vitals and allergies allowed

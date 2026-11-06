Feature: P1 security review: threat-model update, ASVS checks, authorisation tests, dependency scan - no open high/critical
  As a developer, I want a security review before real demographics enter production, so that no serious finding is open.

  # chunk: Run the P1 security review
  @todo
  Scenario: No open high or critical findings
    Then no high or critical finding is open
    And medium findings have dated remediation tickets

  @todo
  Scenario: IDOR suite covers every P1 endpoint
    Then the IDOR test suite covers every P1 endpoint taking a patient or appointment id

  @todo
  Scenario: Public endpoints are rate-limited and pass DAST
    Then landing, demo, TV display and webhook endpoints are rate-limited
    And they pass DAST

  @todo
  Scenario: Review report is stored and linked
    Then the review report is stored in docs/compliance
    And linked in the R1 release notes

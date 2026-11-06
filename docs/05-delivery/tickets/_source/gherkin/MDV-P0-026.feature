Feature: OWASP ASVS L2 baseline: security headers, CSP, HSTS, rate limiting, input validation
  As a developer, I want a baseline of web security controls, so that common attacks are blocked by default.

  # chunk: Add security headers, rate limits, validation and ASVS checklist
  @done
  Scenario: Security headers score A
    When an automated header check runs against staging
    Then it scores A

  @done
  Scenario: Sign-in and OTP are rate limited
    When requests exceed the configured threshold per IP or per account
    Then sign-in and OTP endpoints return 429

  @done
  Scenario: Every command has a validator
    When a command has no validator
    Then an architecture test fails

  @done
  Scenario: ASVS checklist is reviewed
    Then the ASVS L2 checklist marks each control Done, Planned (phase) or N/A
    And it is reviewed in the R0 gate

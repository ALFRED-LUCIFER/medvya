Feature: Secure development policies: SECURITY.md, code review rules, synthetic-only data, JIT access, offline break-glass account
  As a founder, I want security policies in place, so that the team works safely from day one.

  # chunk: Add security policies, JIT access and break-glass procedure
  @done
  Scenario: Policy documents exist and are linked
    Then SECURITY.md, docs/compliance/secure-development.md and the synthetic data policy exist
    And they are linked from README

  @done
  Scenario: No standing privileged access in production
    Then PIM/JIT configuration or a documented procedure means no standing Owner/Contributor or DB admin access in production

  @done
  Scenario: Break-glass account is controlled
    Then a break-glass account exists and is excluded from conditional access only as documented
    And its credentials are stored offline
    And its use triggers an alert

  @done
  Scenario: CI rejects real-looking data
    When a fixture contains a valid national ID checksum or obvious real data markers
    Then the CI check rejects it

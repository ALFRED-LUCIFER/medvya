Feature: Synthetic data generator and seeded staging tenant
  As a developer, I want a seeded synthetic tenant, so that I can test and demo without real patient data.

  # chunk: Seed idempotently and never against production
  @done
  Scenario: Re-seeding is idempotent and production-safe
    When I seed again
    Then the result is the same
    And seeding never runs against production, guarded by an environment check and a test

  # chunk: Seed signable users, synthetic markers and the package listing
  @todo
  Scenario: The Azure dev environment has a synthetic tenant made through the product
    Given the founder signed up and ran onboarding as Dr. Arjun Rao with the runbook's synthetic values
    And invited one persona per role, each accepting with their own Entra account
    Then "Iyer Family Clinic (synthetic)" has one user per role
    And each can sign in with MFA

  @todo
  Scenario: Seeded records are marked synthetic
    Then all seeded records carry a synthetic marker
    And they use invalid-by-construction identifiers that fail real checksums

  @todo
  Scenario: Staging details are in the R0 package
    Then the staging URL and seed accounts are listed in the R0 delivery package
    And secrets are shared out of band

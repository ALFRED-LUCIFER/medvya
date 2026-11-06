Feature: SSO (SAML/OIDC via Entra federation), SCIM provisioning and facility/department role grants with expiry (H-03)
  As hospital IT, I want SSO, SCIM and time-limited facility role grants, so that access follows the hospital's identity system.

  # chunk: Federate the hospital IdP and enforce SSO
  @todo
  Scenario: SSO-enforced tenant signs in through the hospital IdP
    Given a tenant federated with the hospital IdP and SSO enforced
    When a user signs in
    Then they authenticate through the hospital IdP

  @todo
  Scenario: Deactivated IdP user loses access within the SCIM cycle
    Given a user provisioned by SCIM
    When the user is deactivated in the hospital IdP
    Then Medvya access is removed within the SCIM cycle

  # chunk: Grant roles per facility with expiry
  @todo
  Scenario: Role alone never grants another facility
    Given a user with a role at facility A only
    When the user tries to act at facility B
    Then access is denied (tests)

  @todo
  Scenario: Expired grants end access automatically
    Given a locum grant with an end date
    When the end date passes
    Then access ends automatically

  # chunk: Show the grants table, licence count and access review
  @todo
  Scenario: Grants table and quarterly access review
    Given grants of user, role, facility scope and expiry
    When hospital IT opens H-03
    Then the grants table and licence count are shown
    And a quarterly access review can be recorded

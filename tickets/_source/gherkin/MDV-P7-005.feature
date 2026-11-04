Feature: Hospital integration console: connection tiles for HIS, LIS, ABDM/HIE, SSO, messaging (H-01)
  As hospital IT, I want connection tiles showing real health, so that I can monitor and secure each integration.

  # chunk: Show connection tiles driven by real health checks
  @todo
  Scenario: Tile status reflects real health and message flow
    Given connections for HIS, LIS, ABDM/HIE, SSO and messaging
    When health checks and messages change
    Then each tile shows Healthy, Degraded or Not connected accordingly
    And it shows protocol, last message, 24 h volume and direction

  @todo
  Scenario: Residency banner and record-authority switch
    Given a deployment with a data region
    When hospital IT opens H-01
    Then the residency banner and the record-authority switch are shown

  # chunk: Keep connection secrets in Key Vault with audited rotation
  @todo
  Scenario: Connection secrets live in Key Vault and rotation is audited
    Given a connection with a secret
    When the secret is stored and later rotated
    Then it lives in Key Vault only
    And the rotation is audited

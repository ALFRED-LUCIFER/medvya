Feature: Dedicated hospital stamp: own compute, database, Key Vault (CMK/HSM), storage, private link; AKS move when required
  As a developer, I want a repeatable dedicated hospital deployment, so that hospital data is isolated from pooled tenants.

  # chunk: Create a stamp from the Bicep template
  @todo
  Scenario: New stamp is created without manual steps
    Given the stamp template
    When a new dedicated hospital stamp is created
    Then it is created with no manual steps
    And it is registered in the control plane for routing

  # chunk: Isolate hospital data, keys, logs and backups
  @todo
  Scenario: Hospital resources are isolated from pooled tenants
    Given a dedicated hospital stamp
    When its data, keys, logs and backups are inspected
    Then they are isolated from pooled tenants
    And keys are customer-managed in Managed HSM

  # chunk: Connect privately to the HIS
  @todo
  Scenario: Stamp reaches the HIS over private link or VPN
    Given a dedicated hospital stamp
    When the HIS connection is tested
    Then traffic uses private link or VPN

  # chunk: Record the AKS move decision
  @todo
  Scenario: AKS move is recorded with evidence
    Given stamp count or isolation needs may require AKS
    When the decision to move from App Service to AKS is made
    Then it is recorded with the triggering evidence

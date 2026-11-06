Feature: Security operations: incident response with DPDP/UAE breach notification, Defender + Sentinel alerts to on-call, Customer Lockbox, VDP, ADHICS evidence, ISO 27001 gap assessment
  As the founder or developer, I want security operations in place, so that incidents are handled and reported.

  # chunk: Test incident response plan
  @todo
  Scenario: Tabletop with timings
    Given the IR plan
    When a breach scenario tabletop is run
    Then timings are recorded

  # chunk: Add alerting to on-call
  @todo
  Scenario: Alerts within 15 minutes
    Given bulk export, anomalous access and privilege escalation
    When they occur in a test
    Then alerts reach on-call within 15 minutes

  # chunk: Publish VDP and gap assessment
  @todo
  Scenario: VDP and Lockbox
    Given the trust centre
    When checked
    Then the VDP is published and Customer Lockbox is enabled
  @todo
  Scenario: ISO gap assessment
    Given the assessment
    When complete
    Then it lists ISO 27001 controls with status and owners

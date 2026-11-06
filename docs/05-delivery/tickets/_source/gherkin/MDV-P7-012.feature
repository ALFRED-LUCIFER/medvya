Feature: SIEM export and privileged access review
  As a developer or hospital security officer, I want audit events in the hospital SIEM and reviewed privileged access, so that security can be evidenced.

  # chunk: Export events, review access and deliver the evidence pack
  @todo
  Scenario: Hospital SIEM receives the agreed event stream
    Given security and audit events
    When they are exported to the hospital SIEM
    Then the SIEM receives the agreed stream with no PHI

  @todo
  Scenario: Privileged access review is completed and recorded
    Given a quarterly privileged access review
    When it is completed
    Then the review is recorded

  @todo
  Scenario: Security evidence pack is delivered
    Given pen test, controls, ADHICS and ISO mappings and DR results
    When the pack is delivered to the hospital
    Then the hospital has the full evidence pack

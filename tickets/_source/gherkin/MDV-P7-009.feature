Feature: Retention and residency policies (H-06) and data subject requests with tamper-evident export (H-10)
  As a privacy officer or hospital IT, I want configurable policies and tracked data requests, so that the hospital meets its retention and subject-rights duties.

  # chunk: Configure retention and residency policies
  @todo
  Scenario: Policies are configurable per hospital tenant and enforced
    Given policy settings for region, CMK rotation, audio, transcripts, medical records, audit retention and cross-border disabled
    When a hospital tenant changes a policy
    Then it applies to that tenant only
    And retention jobs enforce it
    And the labels are shown as configured policy, not certifications

  # chunk: Track data subject requests and legal holds
  @todo
  Scenario: Requests track due dates and outcomes
    Given requests for access, correction, deletion or withdrawing recording consent
    When they are worked in the queue
    Then each shows its due date and outcome
    And withdrawing recording consent records a vendor purge receipt

  @todo
  Scenario: Deletion under legal hold is partially fulfilled
    Given a deletion request on a record under legal hold
    When the request is processed
    Then it shows "Partially fulfilled · legal hold"

  # chunk: Export tamper-evident data
  @todo
  Scenario: Exports are tamper-evident and audited
    Given an access request is fulfilled
    When the export ZIP is produced
    Then it contains a hash manifest
    And the export is audited

Feature: Retention and deletion jobs per country; patient access/correction/erasure requests for clinics
  As a practice owner, I want retention jobs and patient request handling, so that we comply with privacy law.

  # chunk: Add retention jobs with legal hold
  @todo
  Scenario: Delete only expired and not held
    Given records
    When retention jobs run
    Then only data past its period and not under legal hold is deleted
    And deletions are logged without PHI

  # chunk: Add patient access requests
  @todo
  Scenario: Access within deadline
    Given an access request
    When processed
    Then a patient export is produced within the legal deadline, tracked with due dates

  # chunk: Add erasure and consent withdrawal
  @todo
  Scenario: Partial erasure explained
    Given an erasure request under retention law
    When processed
    Then it explains partial fulfilment with status "Partially fulfilled · legal hold"
  @todo
  Scenario: Audio purge receipt
    Given recording consent is withdrawn
    When processed
    Then audio is purged and a purge receipt is recorded

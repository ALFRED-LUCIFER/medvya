Feature: Audio retention purge and legal hold
  As the system or a practice admin, I want audio purged after retention unless on legal hold, so that recordings are not kept longer than allowed.

  # chunk: Add scheduled purge and legal hold
  @todo
  Scenario: Old audio deleted, held skipped
    Given audio older than the tenant retention period (default 30 days)
    When the job runs
    Then it is deleted and logged and held items are skipped
  @todo
  Scenario: Replay after deletion
    Given purged audio
    When evidence replay is requested
    Then "Audio deleted per retention policy" is shown while transcript evidence remains
  @todo
  Scenario: Retention changes prospective and audited
    Given a retention change
    When saved
    Then it applies prospectively and is audited

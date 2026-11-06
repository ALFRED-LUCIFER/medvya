Feature: Practice access log for owners (F-12): who opened which record and when
  As a practice owner, I want to see who opened which record and when, so that I can oversee access to patient data.

  # chunk: Add access log with owner-only access
  @done
  Scenario: Only owners and admins open the log
    When another role opens the log
    Then the server responds 403
    And the attempt is audited

  @done
  Scenario: Filter by patient lists all reads and writes
    When the owner filters by patient
    Then every read and write of that record by anyone is listed with timestamps

  @done
  Scenario: Denied attempts appear
    Given a receptionist tried to open a clinical note
    Then the log shows it with outcome "Denied"

  # chunk: Add audited watermarked export and performance budget
  @done
  Scenario: Exports are audited and watermarked
    When the owner exports the log
    Then the export is audited
    And watermarked with exporter and time

  @todo
  Scenario: Query performance
    Given pilot volume and a 30-day range
    Then query p95 is under 600 ms

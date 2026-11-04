Feature: Migration dry run into a test tenant and owner-signed dry-run report (N-04)
  As a practice owner, I want to sign a dry-run report before production load, so that I trust the migration.

  # chunk: Add dry-run report and owner sign-off
  @todo
  Scenario: Production blocked until signed
    Given a mapping version
    When no dry-run report for it is signed by the owner
    Then production load is impossible (exit gate 2)
  @todo
  Scenario: Report contents
    Given a dry run
    When it completes
    Then the report shows patient counts, duplicates, records missing EID/ABHA, values failing checks, mapping coverage and sample comparisons

  # chunk: Add re-runnable dry run and stored report
  @todo
  Scenario: Idempotent re-run with deltas
    Given a previous dry run
    When it is re-run
    Then it is idempotent and produces a comparable report with deltas
  @todo
  Scenario: PDF export stored with hash
    Given a report
    When exported
    Then it is a PDF stored with hash

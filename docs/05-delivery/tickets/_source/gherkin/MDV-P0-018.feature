Feature: Observability with OpenTelemetry and PHI redaction (structured logs, telemetry processor)
  As a developer, I want useful telemetry without patient data, so that I can operate the system without leaking PHI.

  # chunk: Redact PHI from telemetry
  @done
  Scenario: Synthetic patient values never appear in telemetry
    When a test request contains a synthetic patient name, phone and Emirates ID
    Then none of those values appear in the telemetry
    And an automated log scan in integration tests verifies this

  @done
  Scenario: Bodies and query strings are not exported
    Then request bodies and query strings are never exported
    And correlation ids are exported

  # chunk: Add staging dashboards and 5xx alert
  @todo
  Scenario: Dashboards show service health
    Then staging dashboards show request rate, p95 latency, error rate and dependency failures

  @todo
  Scenario: 5xx rate alert fires
    Given the 5xx rate is above 2 percent over 5 minutes in staging or prod
    Then an alert fires

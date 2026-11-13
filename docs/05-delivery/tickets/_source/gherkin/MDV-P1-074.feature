Feature: Keep Azure logs flowing all day: PostgreSQL audit and connection logs must not exhaust the Log Analytics daily cap
  As a developer, I want application and job logs kept all day, so that an incident in the pilot is never invisible.

  # chunk: Stop pgaudit READ and size the log cap
  @todo
  Scenario: A normal day stays under the cap
    Given a normal day on Azure dev
    Then ingestion stays under the daily cap and the workspace is never OverQuota

  @todo
  Scenario: SELECTs are not audited
    When the app role runs a SELECT
    Then no pgaudit READ line is written

  @todo
  Scenario: Job logs are kept at any hour
    When a Container Apps job finishes at any hour
    Then its console log is in Log Analytics

  @todo
  Scenario: Audit lines carry no values
    When pgaudit WRITE lines are sampled
    Then none holds a parameter value

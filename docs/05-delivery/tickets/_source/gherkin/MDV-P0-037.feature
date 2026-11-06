Feature: Support and reporting access without dynamic masking: reporting role, masked views, no decrypt right
  As developer, I want this delivered, so that the move to PostgreSQL is safe.

  # chunk: Support and reporting access without dynamic masking: reporting role, 
  @todo
  Scenario: Check 1
    Given the reporting role
    When it queries a patient view
    Then identifiers and contact fields are masked
  @todo
  Scenario: Check 2
    Given the reporting role
    When it tries a base table
    Then the database denies it

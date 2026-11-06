Feature: Money and legal document numbering foundation (minor units, DOC_SEQUENCE per country and financial year)
  As the system, I want money held as integer minor units and legal document numbers issued gaplessly per country and financial year, so that every invoice and receipt is exact and legally numbered.

  # chunk: Add money value object and gapless document numbering
  @done
  Scenario: No floating-point money
    Given the domain and database types
    When the architecture test checks property types
    Then no money property or column uses floating-point or decimal types
  @done
  Scenario: Concurrent invoices get consecutive numbers
    Given 100 invoices are issued concurrently
    When all are issued
    Then they receive 100 consecutive numbers
    And there are no gaps or duplicates
  @done
  Scenario: Financial year rollover
    Given a sequence with a configured prefix
    When a new financial year starts
    Then a new sequence starts for that prefix
  @done
  Scenario: Amount in words
    Given the design examples for INR and AED
    When an amount is rendered in words
    Then it matches the design examples

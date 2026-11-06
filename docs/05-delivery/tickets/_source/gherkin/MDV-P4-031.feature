Feature: Invoice and receipt formats reviewed by a chartered accountant (India) and tax adviser (UAE)
  As the founder, I want advisers to approve the document formats, so that invoices are tax-compliant.

  # chunk: Obtain adviser approval of formats
  @todo
  Scenario: Written approval on file
    Given the sample documents sent to both advisers
    When they respond
    Then written approval from both is on file, or UAE is marked not-in-scope by ADR-002 with a plan
  @todo
  Scenario: Changes re-approved before R4
    Given requested changes
    When implemented
    Then they are re-approved before R4

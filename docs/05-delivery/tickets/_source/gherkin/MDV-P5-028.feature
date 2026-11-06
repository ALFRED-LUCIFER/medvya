Feature: Commercial readiness: order form and SLA signed with the first paying clinic
  As the founder, I want signed commercial terms, so that the first paying clinic is covered before cut-over.

  # chunk: Sign commercial documents
  @todo
  Scenario: Signed documents on file
    Given the first paying clinic
    When cut-over approaches
    Then the signed order form, SLA and DPA are on file before cut-over
  @todo
  Scenario: SLA matches measured capability
    Given the SLA
    When compared with measurements
    Then its numbers match the measured capabilities (99.5 %, < 4 h response, RPO/RTO)

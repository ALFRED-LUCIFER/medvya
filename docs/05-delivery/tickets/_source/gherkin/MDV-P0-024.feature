Feature: Threat model, data map, DPIA draft (DPDP) and record-retention map per country
  As a founder, I want security and privacy documents drafted, so that risks and obligations are known before real data.

  # chunk: Write the threat model
  @done
  Scenario: Threat model covers every trust boundary
    Then the threat model covers every trust boundary in docs/03-architecture/README.md
    And each threat has at least one mitigation with links to tickets

  # chunk: Write the data map and DPIA draft
  @done
  Scenario: Data map lists every category
    Then the data map lists every P0-P2 data category with storage location, country and processor

  @done
  Scenario: DPIA draft is complete
    Then the DPIA draft lists purposes, lawful basis/consent, data minimisation, retention and risks
    And open items are assigned

  # chunk: Write the retention map and package all four documents
  @done
  Scenario: Retention map covers each data class
    Then the retention map has a row for each data class with proposed default and legal-review status

  @done
  Scenario: Documents are in the R0 package
    Then all four documents are part of the R0 delivery package

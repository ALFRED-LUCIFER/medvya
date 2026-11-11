Feature: Data classification register and ERD -> PostgreSQL type mapping
  As a founder, I want every field classified and mapped, so that controls and the data map are provable.

  # chunk: Add classification attribute, register and type mapping
  @done
  Scenario: Every P0 column is classified
    When a new entity property lacks a classification attribute
    Then a unit test fails

  @done
  Scenario: Register lists encryption and masking sets
    Then the register lists the encrypted national-ID column set and the masked-view set used by MDV-P0-009

  @done
  Scenario: ERD to PostgreSQL mapping is closed
    Then the mapping table closes the PRD item "ERD mapped to Azure SQL types" with status Covered

  @done
  Scenario: Data map is in the P0 package
    Then the data map shows what data, where stored, which country and which processors
    And it is published in the P0 delivery package

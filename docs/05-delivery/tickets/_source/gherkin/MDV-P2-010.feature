Feature: Diagnosis coding with ICD-10 and problem verification states (E-07)
  As a doctor, I want to code diagnoses with ICD-10 and verification states, so that the problem list is accurate and claim-ready later.

  # chunk: Add ICD-10 search and problem records
  @todo
  Scenario: Fast ICD-10 search
    Given the problem editor
    When the doctor searches by code or words
    Then results return in under 300 ms
  @todo
  Scenario: Problem stores its context
    Given a saved problem
    When it is read back
    Then it holds code system/version, verification state, author and encounter
  @todo
  Scenario: Provisional problems are distinct
    Given a provisional problem
    When shown or exported
    Then it is visually distinct and not exported as a confirmed diagnosis
  @todo
  Scenario: Code set version per country
    Given a country
    When the code set version is configured
    Then the decision is recorded

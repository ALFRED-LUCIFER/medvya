Feature: Load the pilot clinic's brand names into the formulary, mapped to generic and class; offer generic matches for typed text
  A brand typed as free text skips the allergy check.

  # chunk: Load the pilot clinic's brand names into the formulary, mapped to generic and class; offer generic matches for typed text
  @todo
  Scenario: A brand finds its generic
    Given the loaded list
    When the doctor searches Augmentin
    Then the amoxicillin and clavulanate row is offered with the brand shown

  @todo
  Scenario: The class check applies to the brand
    Given a patient with a penicillin allergy
    When that brand row is chosen
    Then it is blocked in the dialog and at authorisation exactly as the generic is

  @todo
  Scenario: Did you mean comes before free text
    Given typed text with no exact match
    When the list opens
    Then the closest formulary rows are offered above Add as free text

  @todo
  Scenario: Every row is reviewed
    Given the imported list
    When the issue is closed
    Then each brand's generic and class mapping carries a named clinical reviewer and date

  @todo
  Scenario: Tenant isolation
    Given two clinics
    When one loads its brands
    Then the other clinic's formulary and search do not show them (integration test with row-level security)

  @todo
  Scenario: Languages and axe
    Given the search list in English and Arabic
    When axe runs
    Then there is no violation

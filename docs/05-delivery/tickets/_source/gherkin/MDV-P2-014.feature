Feature: Country formulary and order sets: India (Schedule H/H1/X) and UAE drug codes, admin add medicine (F-13)
  As a practice admin or doctor, I want a country formulary and order sets, so that prescribing uses correct, classified medicines.

  # chunk: Record the formulary data source decision
  @todo
  Scenario: Source and licence decided first
    Given a country
    When formulary seeding is planned
    Then the data source and licence are recorded in an ADR before seeding (dependency 1 closed)

  # chunk: Add formulary search and regulatory classes
  @todo
  Scenario: Fast search with options
    Given the formulary
    When the doctor searches by generic or brand
    Then strength and form options return in under 300 ms
  @todo
  Scenario: Controlled classes drive print rules
    Given a Schedule H or controlled medicine
    When it is shown or printed
    Then its class is labelled and drives print requirements such as Schedule H warning text

  # chunk: Add admin-added medicines and order sets
  @todo
  Scenario: Clinic-added medicines are audited
    Given an admin adds a medicine
    When it is saved
    Then it is audited and marked "clinic-added"
  @todo
  Scenario: Order sets usable in composer
    Given an order set such as "Diabetes review labs"
    When created
    Then it can be used in the order composer

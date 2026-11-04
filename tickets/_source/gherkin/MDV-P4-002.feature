Feature: Services and fees catalogue with price versions, India GST/SAC, UAE CPT/VAT, payer price lists and packages (F-11)
  As a practice admin, I want a services catalogue with versioned prices and tax codes, so that invoices carry correct prices and tax.

  # chunk: Add price versions to the services catalogue
  @todo
  Scenario: Saving a price creates a version
    Given a service with a price and an issued invoice
    When the admin saves a new price
    Then a new price version is created
    And the issued invoice keeps the version it was issued with
  @todo
  Scenario: Only admins edit, with audit
    Given a non-admin user
    When they try to edit the catalogue
    Then the change is refused
    And admin changes are audited

  # chunk: Add India and UAE tax codes and payer price lists
  @todo
  Scenario: India tax fields
    Given an India service
    When it is saved
    Then it carries a SAC and a GST rate or exemption
  @todo
  Scenario: UAE tax fields
    Given a UAE service
    When it is saved
    Then it carries a CPT and a VAT rate of 5 % or zero-rated per line
  @todo
  Scenario: Payer price list overrides default
    Given a payer-specific price list for a UAE insurer
    When an insured patient is charged
    Then the payer price overrides the default price

  # chunk: Add sellable packages
  @todo
  Scenario: Packages sellable from billing
    Given a package defining sessions and validity
    When the receptionist opens billing
    Then the package can be sold

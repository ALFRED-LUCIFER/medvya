Feature: Receipts, tax invoices, credit notes, advance receipts for India GST and UAE VAT in A5 and 80 mm thermal (P-15)
  As a receptionist, I want legally formatted receipts and invoices in A5 and thermal layouts, so that I can print or share them.

  # chunk: Add India and UAE document layouts
  @todo
  Scenario: Every document type renders
    Given each document type for India and UAE
    When it is rendered
    Then it matches the design fields
    And the snapshots are approved
  @todo
  Scenario: Layouts print correctly
    Given the thermal and A5 layouts
    When printed
    Then the thermal layout prints correctly on an 80 mm printer and A5 on standard printers

  # chunk: Add stored hashed documents and adviser review
  @todo
  Scenario: Documents re-printable identically
    Given an issued document
    When it is stored
    Then it is stored with a hash
    And re-printing gives an identical document
  @todo
  Scenario: Adviser approval
    Given the document formats
    When a chartered accountant (India) and a UAE tax adviser review them
    Then both approve the formats (exit gate 2; MDV-P4-031)

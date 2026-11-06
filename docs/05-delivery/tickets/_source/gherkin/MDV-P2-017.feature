Feature: Rx authorisation (separate from note), server-side multilingual printed Rx A4/A5 with hash and QR verify (E-10)
  As a doctor, I want to authorise and print a multilingual prescription, so that patients get a verifiable Rx.

  # chunk: Add authorisation separate from the note
  @todo
  Scenario: Not printable until authorised
    Given an unauthorised Rx
    When the doctor tries to print or share it
    Then it is refused
    And authorisation (with step-up) is audited

  # chunk: Add server-side A4/A5 PDF with hash and QR verify
  @todo
  Scenario: PDF stored with hash
    Given an authorised Rx
    When the PDF is generated for A4 and A5
    Then it renders server-side and is stored with a hash
  @todo
  Scenario: QR verify shows validity only
    Given the QR code on the Rx
    When scanned
    Then the verify page shows validity without exposing PHI beyond what the patient holds

  # chunk: Add pilot language printing
  @todo
  Scenario Outline: Print language
    Given an Rx printed in <language>
    When the PDF is produced
    Then instructions appear in English plus <language>, with RTL correct for Arabic
    Examples:
      | language |
      | Hindi |
      | Tamil |
      | Arabic |

  # chunk: Add per-doctor layout approval and preview accessibility
  @todo
  Scenario: Each doctor approves own layout
    Given a pilot doctor
    When they review their Rx layout
    Then the approval is recorded per doctor (exit gate 4)
  @todo
  Scenario: Preview accessibility
    Given the on-screen preview
    When checked at 200 % zoom and with screen-reader labelling
    Then it passes

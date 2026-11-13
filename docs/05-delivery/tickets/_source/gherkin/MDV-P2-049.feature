Feature: Signed note print: vitals, allergies and diagnoses by default, and a page heading
  The print shows identity and the note sections but not the nurse's vitals, the allergy, the coded diagnosis or the medicines; vitals enter only if the doctor pressed Add to Objective.

  # chunk: Signed note print: vitals, allergies and diagnoses by default, and a page heading
  @todo
  Scenario: The record is complete
    Given a signed note for a visit with nurse vitals, a penicillin allergy and a coded diagnosis
    When it is printed
    Then all three are on the print with their sources

  @todo
  Scenario: Unknown is not none
    Given allergies not recorded at signing
    When the note is printed
    Then it says not recorded and never none

  @todo
  Scenario: Signed means signed
    Given a vital corrected after signing
    When the note is printed
    Then the print shows the value as at signing, and the test fails if later data replaces it silently

  @todo
  Scenario: One heading
    Given the print view at five widths
    When axe runs
    Then there is no violation and the page has one h1

  @todo
  Scenario: Clinical agreement recorded
    Given the issue
    When it is closed
    Then it holds the pilot doctor's dated agreement on the contents

  @todo
  Scenario: Languages
    Given English and Arabic
    When the print renders
    Then it mirrors and prints on A4 at 200 % zoom without loss

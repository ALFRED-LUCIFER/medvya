Feature: After the identity step-up the doctor returns to the section that asked, with focus on its button
  After confirming identity the page was at the top (scrollY 0), the prescription out of view, and Authorise had to be found and pressed a second time.

  # chunk: After the identity step-up the doctor returns to the section that asked, with focus on its button
  @todo
  Scenario: Back where it started
    Given a doctor who pressed Authorise and confirmed identity
    When the page returns
    Then the prescription section is in view and focus is on Authorise

  @todo
  Scenario: Told in words
    Given the return
    When the status is read by sight or screen reader
    Then it says identity was confirmed and what to press

  @todo
  Scenario: Still deliberate
    Given the return
    When nothing is pressed
    Then nothing is authorised

  @todo
  Scenario: No patient data in the address
    Given the round trip
    When every address and the provider's state value are read
    Then they hold only opaque identifiers

  @todo
  Scenario: Works with real Entra
    Given Azure dev
    When the founder runs the path with MFA
    Then the return lands on the section

  @todo
  Scenario: Languages and axe
    Given English and Arabic
    When axe runs after the return
    Then there is no violation

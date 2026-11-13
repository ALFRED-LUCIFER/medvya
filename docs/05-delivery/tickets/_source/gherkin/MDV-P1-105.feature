Feature: Redeploy Azure dev from main, capture it again at 768 and 375 px, and mark its synthetic patients
  What is deployed is behind what is built: on Azure dev the header scrolls sideways at 768 and 375 px (page width 939 and 511 px) and hides Sign out; main does not.

  # chunk: Redeploy Azure dev from main, capture it again at 768 and 375 px, and mark its synthetic patients
  @todo
  Scenario: No sideways scroll
    Given Azure dev after the deploy
    When every captured page is measured at 768 and 375 px
    Then page width equals viewport width

  @todo
  Scenario: Sign out reachable
    Given the same widths
    When the header is used
    Then Sign out can be reached

  @todo
  Scenario: Deployed is current
    Given the deployed version
    When it is compared with main
    Then it is the tagged commit and main was green

  @todo
  Scenario: Synthetic is marked
    Given the Azure dev patients
    When Today and the board are opened
    Then every patient name carries (synthetic)

  @todo
  Scenario: Terms status checked
    Given Settings → Terms on Azure
    When booking is tried
    Then the result (status line right or wrong) is written on the small-fixes ticket

  @todo
  Scenario: No secrets or real data
    Given the capture output
    When it is stored
    Then it holds synthetic data only and no credential; the founder signs in by hand

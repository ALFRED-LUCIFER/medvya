Feature: Public site on Razor Pages and landing page (solo doctor / hospital CTAs, region-aware copy)
  As a public visitor, I want a fast landing page with region-aware copy, so that I can choose between starting a trial as a doctor or booking a demo as a hospital.

  # chunk: Add public landing page with region switch and localisation
  @done
  Scenario: Landing page works without JavaScript and scores well on Lighthouse
    Given JavaScript is disabled
    When a visitor opens the landing page on mobile
    Then the page renders fully
    And Lighthouse performance and accessibility scores are at least 90

  @done
  Scenario Outline: Region switch changes the regional copy
    Given a visitor is on the landing page
    When the visitor switches the region to <region>
    Then the hero tag, language list and residency line match the F-01 screen spec for <region>
    And the choice is remembered with a non-tracking cookie
    Examples:
      | region |
      | India  |
      | UAE    |

  @done
  Scenario: Only the two designed calls to action are above the fold
    Given a visitor opens the landing page
    Then only the two designed CTAs exist above the fold
    And the Pricing page shows a placeholder with no invented prices or metrics

  @done
  Scenario Outline: Pages are available in three languages with SEO metadata
    When a visitor opens the landing page in <language>
    Then the page is translated with SEO metadata
    And no third-party tracker loads without consent
    Examples:
      | language           |
      | English            |
      | Hindi              |
      | Arabic (right-to-left) |

  @done
  Scenario: Public pages expose no PHI or tenant data
    When a visitor opens any public page
    Then no patient or tenant data is reachable
    And the public pages use a separate route group that does not read the auth cookie

Feature: Trust centre and demo request (no PHI)
  As a hospital buyer, I want a trust centre and a demo request form, so that I can assess controls and contact the company without sharing patient data.

  # chunk: Add trust centre and demo request form
  @done
  Scenario: Trust centre blocks render with effective dates
    When a visitor opens the trust centre
    Then all six blocks render with the designed copy
    And policy versions and sub-processors show effective dates

  @done
  Scenario: No page claims a certification
    Given public copy and the content file
    When the certification lint check runs
    Then no phrase such as "ISO 27001 certified" or "HIPAA compliant" is found

  @done
  Scenario: Demo requests are stored, emailed, rate-limited and bot-protected
    When a visitor submits a demo request
    Then the request is stored and emailed to the founder
    And the form warns not to include patient information
    And repeated submissions are rate-limited and bots are rejected

  @done
  Scenario: Trust centre content is editable without redeploy and versioned
    Given policy and sub-processor content lives in Markdown files or settings
    When the content changes
    Then the page shows the new content without a redeploy
    And the change is versioned

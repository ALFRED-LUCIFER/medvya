Feature: Public pricing page with 14-day trial, monthly and yearly toggle and country currency
  As public, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Public pricing page with 14-day trial, monthly and yearly toggle and c
  @todo
  Scenario: Check 1
    Given a visitor in the UAE
    When opens pricing
    Then prices show in AED with Apple Pay and cards listed
  @todo
  Scenario: Check 2
    Given a visitor
    When chooses Clinic and starts a trial
    Then sign-up opens with that plan
  @todo
  Scenario: Check 3
    Given a price changes in the console
    When the page reloads
    Then the new price shows

Feature: Plans and entitlements: versioned plans, India and UAE prices, seats, trial days, 30 days' notice
  As medvya operations, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Plans and entitlements: versioned plans, India and UAE prices, seats, 
  @todo
  Scenario: Check 1
    Given a plan is edited
    When it is saved
    Then a new version exists and current subscribers keep the old one until renewal
  @todo
  Scenario: Check 2
    Given a price changes
    When a subscriber exists
    Then they are notified 30 days before it applies
  @todo
  Scenario: Check 3
    Given a feature is removed from a plan
    When data exists
    Then the data stays

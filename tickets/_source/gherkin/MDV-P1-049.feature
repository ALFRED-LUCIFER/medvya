Feature: Service worker for static assets only (app shell loads with no network, no patient data)
  As a receptionist, I want the app to open with a clear offline state, so that a reload during an outage is not a blank browser error.

  # chunk: Cache the app shell only
  @todo
  Scenario: Reload with no network
    Given the user opened the app earlier today
    When the network is off and the page reloads
    Then the app shell loads with the offline state and no patient data is shown
    And browser storage contains no patient identifiers (automated check)

  @todo
  Scenario: A release replaces the cache
    When a new version is deployed
    Then the cached files are replaced and no stale shell is shown

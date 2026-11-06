Feature: System state screens: session expired, 403 no facility access, 404, offline
  As a staff member, I want clear screens when something is unavailable, so that I know what happened and what to do next.

  # chunk: Show 404 and offline states accessibly
  @done
  Scenario: Unknown route shows page not found
    When I open an unknown route
    Then F-05c is shown
    And it states that nothing has been changed

  @done
  Scenario: Going offline keeps form input
    Given I have typed into a form
    When I go offline
    Then F-05d is shown without losing my input
    And reconnecting restores the page

  @done
  Scenario: States are accessible and translated
    Then each state is keyboard-accessible and announced to screen readers
    And it is translated in en, hi and ar
    And Arabic uses RTL layout

  # chunk: Show 403 state and store an access request
  @todo
  Scenario: A 403 shows no-access with a request action
    When any API returns 403
    Then F-05b shows "You don't have access to this facility." (microcopy key err.403)
    And a "Request access" action creates an access request that is stored and audited

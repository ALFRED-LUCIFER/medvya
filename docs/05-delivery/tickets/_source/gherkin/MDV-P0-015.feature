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

  # chunk: React to 401 and 403 from any API call
  @todo
  Scenario: A 401 from any call shows session expired at once
    Given I am signed in at a facility
    When any API returns 401
    Then F-05a is shown without waiting for the session poll

  @todo
  Scenario: A 403 after losing the membership leaves the facility
    Given my membership at this facility was revoked or suspended
    When any API returns 403
    Then the session is re-read and I am no longer working at that facility

  @todo
  Scenario: A 403 for a missing capability keeps the screen's refusal
    Given my role lacks the capability for this screen
    When its API returns 403
    Then the screen shows its own refusal (microcopy key err.403) and my facility stays active

  # chunk: Show no-access to a removed member
  @todo
  Scenario: A member removed from their only clinic sees no-access
    Given I was removed from my only clinic
    When I sign in or my next call is refused
    Then F-05b shows "You don't have access to this facility." (microcopy key err.403)
    And the practice setup wizard is not shown

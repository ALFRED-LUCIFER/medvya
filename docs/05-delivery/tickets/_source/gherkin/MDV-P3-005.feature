Feature: Capture on phone (mobile web) or desktop mic with timer, pause, consent indicator and warnings (A-03, X-01)
  As a doctor, I want to capture audio on phone or desktop with clear state, so that I always know recording is on.

  # chunk: Add capture controls mirrored server-side
  @todo
  Scenario: FSM transitions mirrored
    Given start, pause and stop actions
    When used
    Then transitions follow the capture FSM and are mirrored server-side
  @todo
  Scenario: Indicator always visible
    Given capture is running, including on mobile
    When the screen is viewed
    Then the recording indicator and timer are visible at all times

  # chunk: Add privacy and warnings
  @todo
  Scenario: No PHI in notifications or title
    Given recording is active
    When OS notifications or the page title are shown
    Then they contain no PHI
  @todo
  Scenario Outline: Warning with guidance
    Given <condition> during capture
    When it occurs
    Then a warning with text guidance appears
    Examples:
      | condition |
      | battery below 20 % |
      | offline or poor network |
      | low audio level |

  # chunk: Verify browser matrix
  @todo
  Scenario: Supported browsers
    Given current Chrome/Edge desktop, Android Chrome and iOS Safari
    When capture is tested
    Then it works and the tested matrix is recorded

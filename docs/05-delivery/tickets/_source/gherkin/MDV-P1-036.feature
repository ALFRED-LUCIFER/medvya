Feature: Tablet-responsive reception views (desk tablet)
  As a receptionist, I want every reception screen to work on a desk tablet, so that I can run the desk without a PC.

  # chunk: Verify tablet layouts and touch targets
  @todo
  Scenario Outline: Visual checks at tablet sizes
    Given a viewport of <size>
    When every P1 screen is opened
    Then Playwright visual checks pass without clipped controls
    Examples:
      | size      |
      | 768x1024  |
      | 1024x768  |

  @todo
  Scenario: Touch targets are at least 44 px
    Then all touch targets are at least 44 px (automated check)

  @todo
  Scenario: Registration and check-in on tablet emulation
    Given a tablet emulation
    Then registration and check-in journeys complete end-to-end

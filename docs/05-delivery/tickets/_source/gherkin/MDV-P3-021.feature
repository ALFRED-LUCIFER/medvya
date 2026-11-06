Feature: Mobile capture and note review (swipe to accept, evidence bottom sheet) (X-01)
  As a doctor, I want to capture and review notes on my phone, so that I can finish documentation anywhere.

  # chunk: Add mobile capture and review frames
  @todo
  Scenario: Mobile frames and targets
    Given Today, Capture, Note review, Appointments and Patient record on a phone
    When used at 360 to 430 px
    Then all work with 48 px targets and a sticky identity banner

  # chunk: Add accessible swipe and pinned sign
  @todo
  Scenario: Swipe has a button alternative
    Given swipe-to-accept
    When used by keyboard or screen reader
    Then an accessible button alternative exists
  @todo
  Scenario: Sign pinned and gated
    Given note review on mobile
    When shown
    Then Sign stays pinned above the tab bar and disabled until 0 unresolved

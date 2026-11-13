Feature: StickyActionBar as a layout footer and SaveStatus, in the shared library
  The floating Review and sign bar covers fields and hid 2 of 45 tab stops (WCAG 2.2 SC 2.4.11 at risk).

  # chunk: StickyActionBar as a layout footer and SaveStatus, in the shared library
  @todo
  Scenario: The footer takes its own space
    Given a long page in the gallery with the footer
    When the last field is scrolled to
    Then the whole field is visible above the footer

  @todo
  Scenario: Focus is never hidden
    Given the gallery page
    When every control is reached with Tab
    Then each focused element is fully inside the visible area and none is under the footer

  @todo
  Scenario: Three honest states
    Given SaveStatus
    When it is set to saving, saved at 10:42 and failed
    Then it reads Saving…, Saved 10:42 and Not saved. Trying again., and it cannot render Saved without a time

  @todo
  Scenario: Announced, not animated
    Given a screen reader and reduced motion
    When the state changes
    Then the new text is announced once and swaps with no movement

  @todo
  Scenario: Un-pins at high zoom
    Given 400 % zoom
    When the page scrolls
    Then the footer scrolls with the content

  @todo
  Scenario: Mirrors and passes axe
    Given the gallery in English and Arabic
    When axe runs
    Then there is no violation

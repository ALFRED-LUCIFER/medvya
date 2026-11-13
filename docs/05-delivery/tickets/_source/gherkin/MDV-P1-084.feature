Feature: Shell components in the library: AccountMenu, the labelled rail item, PageHeader with back and meta slots, and Tooltip
  Language, My devices and Sign out sit as permanent buttons beside patient search; the rail between 768 and 1279 px is icons with no labels; page titles sit at seven different heights and jump when data loads.

  # chunk: Shell components in the library: AccountMenu, the labelled rail item, PageHeader with back and meta slots, and Tooltip
  @todo
  Scenario: Account menu by keyboard
    Given the gallery
    When the avatar button is focused and Enter is pressed
    Then the menu opens, arrow keys move through language, my devices, change facility and sign out in that order, Escape closes it and focus returns to the button

  @todo
  Scenario: Sign out is last and apart
    Given the open menu
    When it is read
    Then sign out is the last item, visually separated, and every item is at least 44 px tall

  @todo
  Scenario: The rail is labelled
    Given NavRailItem at 72 px
    When it renders in English and Arabic
    Then the label is visible text under the icon and is not truncated for the longest translated name

  @todo
  Scenario: The header does not jump
    Given PageHeader with a title, a date and two actions
    When the date arrives after loading
    Then the title and actions do not move and the row stays 44 px

  @todo
  Scenario: Tooltip is an addition, not the label
    Given an icon button with a Tooltip
    When it is focused by keyboard
    Then the tooltip shows and the button still has an accessible name without it

  @todo
  Scenario: Accessible and mirrored
    Given the gallery in English and Arabic
    When axe runs
    Then there is no violation

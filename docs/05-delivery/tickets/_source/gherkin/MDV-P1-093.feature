Feature: App shell: one account menu, a 48 px top bar and a labelled rail from 768 to 1279 px
  Controls used once a day hold the most valuable row, Sign out sits beside Search, and this row is what overflowed on Azure dev at 768 and 375 px.

  # chunk: App shell: one account menu, a 48 px top bar and a labelled rail from 768 to 1279 px
  @todo
  Scenario: No sideways scroll and Sign out reachable
    Given any signed-in page at 768 and 375 px
    When it loads
    Then page width equals viewport width and Sign out is reachable in at most two actions

  @todo
  Scenario: The rail is labelled
    Given 1024 by 768 for each role
    When the shell renders
    Then every navigation item shows its name as visible text under the icon

  @todo
  Scenario: Collapse is the user's choice
    Given 1440 px
    When a user has never chosen
    Then the labelled sidebar shows; when they collapse it, then the labelled rail shows and the choice is kept on reload

  @todo
  Scenario: Top bar is 48 px
    Given 1024 px and wider
    When the top bar is measured
    Then it is 48 px tall and holds clinic, search, bell and avatar only

  @todo
  Scenario: Keyboard and focus
    Given the avatar button
    When the menu is opened and closed by keyboard
    Then focus returns to the button and the skip link is still the first tab stop

  @todo
  Scenario: Arabic
    Given Arabic
    When the shell renders
    Then it mirrors, the search key hint reads correctly, and axe finds no violation in English or Arabic

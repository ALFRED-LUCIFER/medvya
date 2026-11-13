Feature: Adopt PageHeader, Card and Section, PageMessage and EmptyState across the clinic app and the platform console
  37 hand-written page titles at seven different heights, 97 inline card strings, 10 local Message functions.

  # chunk: Adopt PageHeader, Card and Section, PageMessage and EmptyState across the clinic app and the platform console
  @todo
  Scenario: One header everywhere
    Given every page in both apps at 1440
    When the title is measured
    Then it sits in a 44 px header row at the same height and content starts within 130 px of the top

  @todo
  Scenario: No jump when data arrives
    Given the board and My day
    When data loads
    Then the title and actions do not move

  @todo
  Scenario: Counts are zero
    Given features
    When the ratchet runs
    Then raw h1, border bg-card, and local Message, Card and Empty are all zero and their lint rules are errors

  @todo
  Scenario: Console sign-in fits a phone
    Given the platform console sign-in at 375 px
    When it renders
    Then page width equals viewport width and controls are at least 44 px

  @todo
  Scenario: More patients on the directory
    Given the patients directory at 1024 by 768
    When it opens
    Then the first row starts at or above 300 px

  @todo
  Scenario: Languages and axe
    Given English and Arabic
    When axe runs on each migrated page
    Then there is no violation and each page has one h1

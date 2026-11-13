Feature: Page components in the library: Card and Section, PageMessage and EmptyState, SegmentedControl, SplitLayout and Sheet
  97 inline card strings, 10 local Message functions, 20 hand-built view switches in five styles and four hand-built two-pane grids.

  # chunk: Page components in the library: Card and Section, PageMessage and EmptyState, SegmentedControl, SplitLayout and Sheet
  @todo
  Scenario: Four page states in one component
    Given /__ui
    When PageMessage is opened
    Then busy, error, empty and denied each show an icon, a sentence and at most one action, in English and Arabic

  @todo
  Scenario: One view switch
    Given SegmentedControl with three options
    When it is used by keyboard
    Then arrow keys or Tab move between options, the selected one is exposed as pressed, and each segment is at least 44 px tall

  @todo
  Scenario: Two card sizes only
    Given Card
    When its props are read
    Then only the page-section and list-item sizes exist and nesting a bordered card in a bordered card is flagged in development

  @todo
  Scenario: Side pane becomes a drawer
    Given SplitLayout at 1024 px and at 768 px
    When the side content is opened
    Then at 1024 it is beside the main pane and at 768 it is a Sheet that traps focus, closes on Escape and returns focus to the opener

  @todo
  Scenario: Reduced motion
    Given reduced motion
    When the Sheet opens
    Then it appears with opacity only and no movement

  @todo
  Scenario: Accessible and mirrored
    Given the gallery in English and Arabic
    When axe runs with the Sheet open and closed
    Then there is no violation and the Sheet opens from the correct side in right-to-left

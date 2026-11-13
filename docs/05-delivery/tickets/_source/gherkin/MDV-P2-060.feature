Feature: Adopt Chip, Tag, Banner and SegmentedControl in features: one chip vocabulary, one view switch
  Three chip styles sit side by side on one Today row; the same switch-the-view interaction has five appearances and its selected state relies on a thicker border.

  # chunk: Adopt Chip, Tag, Banner and SegmentedControl in features: one chip vocabulary, one view switch
  @todo
  Scenario: One vocabulary on a row
    Given a My day row with three states
    When it renders
    Then all three use Chip or Tag from the library

  @todo
  Scenario: One view switch
    Given the board, My day, schedule, registration and the consultation
    When each view switch renders
    Then it is SegmentedControl with the selected state exposed to assistive technology

  @todo
  Scenario: Wording unchanged
    Given every status label before and after
    When the translated strings are compared
    Then none changed

  @todo
  Scenario: One allergy look
    Given the chart, the consultation, the Add medicine dialog and the sign dialog
    When the allergy renders
    Then it is AllergyBlock in each

  @todo
  Scenario: One primary per view
    Given My day and the board
    When the first viewport is counted
    Then at most one filled primary button shows

  @todo
  Scenario: Languages and axe
    Given English and Arabic
    When axe runs
    Then there is no violation and no status relies on colour alone

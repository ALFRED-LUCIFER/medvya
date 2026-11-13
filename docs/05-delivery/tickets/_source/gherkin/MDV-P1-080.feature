Feature: Tone tokens: one colour pair per meaning; no raw hex left in the shared library; the contrast test reads the tokens
  Tokens style the chrome and 103 raw hex colours style the meaning.

  # chunk: Tone tokens: one colour pair per meaning; no raw hex left in the shared library; the contrast test reads the tokens
  @todo
  Scenario: One pair per meaning
    Given the tokens
    When the tone layer is read
    Then danger, warning, success, info, brand, ai and neutral each have a text, background and border value and a written meaning

  @todo
  Scenario: No raw colours in the library
    Given packages/ui/src
    When it is searched for hex colours in class names or style objects
    Then there is none, the ratchet count is zero and the lint rule is an error

  @todo
  Scenario: Looks unchanged
    Given the /__ui gallery
    When every status primitive is captured before and after in English and Arabic
    Then no pixel differs

  @todo
  Scenario: Contrast is tested from the tokens
    Given the contrast test
    When a tone's text colour is changed to one that gives less than 4.5:1
    Then the test fails

  @todo
  Scenario: Status is never colour alone
    Given every primitive that uses a tone
    When it renders
    Then it carries a text label, and an icon where one existed before

  @todo
  Scenario: Web and native share names
    Given packages/ui and packages/ui-native
    When the tone names are compared
    Then they are the same seven

Feature: Portal Arabic RTL and patient help pages in three languages
  As a patient, I want the portal and help in my language including Arabic right-to-left, so that I can use it confidently.

  # chunk: Add Arabic RTL and three-language help pages
  @todo
  Scenario: RTL visual checks
    Given every portal screen
    When checked at 390 px in Arabic
    Then each passes RTL visual checks
  @todo
  Scenario: Help pages in three languages
    Given the portal
    When help is opened
    Then help pages exist in English, Hindi and Arabic and are linked from the portal
  @todo
  Scenario: Native speaker review
    Given the translations
    When reviewed
    Then a native speaker has reviewed them

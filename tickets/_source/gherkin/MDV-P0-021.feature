Feature: Internationalisation pipeline: English, Hindi, Arabic with full RTL
  As any user, I want the app in my language and direction, so that I can use it comfortably.

  # chunk: Add i18n pipeline with RTL, lint rule and locale formatting
  @done
  Scenario: Arabic flips the whole layout
    When I switch to Arabic
    Then layout direction flips for the whole app shell and the gallery without visual overlap
    And a Playwright screenshot test at 360 and 1280 px verifies it

  @done
  Scenario: No hard-coded user-facing strings
    When a component has a JSX text literal outside i18n
    Then the lint rule fails

  @done
  Scenario: Missing translations fall back to English
    When a translation key is missing
    Then English is shown
    And the gap is reported in CI

  @done
  Scenario: Handoff microcopy keys exist
    Then the Handoff microcopy keys exist with the exact English text from docs/02-design/components-theme-copy.md

  @done
  Scenario Outline: Locale-aware formatting
    Given the locale is <locale>
    Then <format> is shown

    Examples:
      | locale         | format                      |
      | India (en-IN)  | rupee with Indian grouping  |
      | UAE            | AED                         |

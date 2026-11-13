Feature: Tokens: nine type sizes, the spacing scale, page and panel widths, the 1024 tier, density and two motion values
  The app renders 12 font sizes against a documented scale of 7, has no maximum page width, and uses four different side-panel widths.

  # chunk: Tokens: nine type sizes, the spacing scale, page and panel widths, the 1024 tier, density and two motion values
  @todo
  Scenario: Tokens exist once
    Given packages/tokens
    When the generator runs
    Then the nine type tokens, the spacing scale, the widths, the density values, the wide breakpoint and the two motion tokens are in tokens.css and theme.ts and usable as Tailwind utilities

  @todo
  Scenario: Nothing reflows
    Given the 20 most-used screens
    When they are captured before and after
    Then no pixel differs

  @todo
  Scenario: Native still builds
    Given mobile/ and packages/ui-native
    When their type check and tests run
    Then they pass with the extended tokens

  @todo
  Scenario: Specimen in both directions
    Given /__ui
    When the type and spacing specimen is opened in English and Arabic
    Then each token is shown with its name and measured size, Arabic uses Noto Sans Arabic, and axe finds no violation

  @todo
  Scenario: Typed clinical text has its own token
    Given the body-large token
    When it is measured
    Then it is 16 px on a 24 px line, so a phone browser does not zoom on focus

  @todo
  Scenario: Generated files are in step
    Given tokens.json
    When packages/tokens tests run
    Then they fail if tokens.css or theme.ts was not regenerated

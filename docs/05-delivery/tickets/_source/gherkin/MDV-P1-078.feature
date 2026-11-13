Feature: Codemod: delete about 350 redundant control-height patches (merges alone), then give radios, checkboxes and name-link rows a 44 px target
  Once the defaults are right, the patches are noise and the next missed patch is impossible.

  # chunk: Codemod: delete the height patches and turn the rule on
  @todo
  Scenario: Patches are gone
    Given web/clinic/src and web/platform-admin/src
    When the code is searched for min-h-11 or h-11 on Input, SelectTrigger, Textarea and Button
    Then there is no hit and the ratchet count is zero

  @todo
  Scenario: The lint rule now blocks it
    Given a feature file
    When a developer adds h-11 to an Input
    Then pnpm -r lint fails with a message that names the library default

  @todo
  Scenario: Nothing moved
    Given the screens captured before the codemod at 1440, 1024 and 375
    When they are captured again after it
    Then no control changed size or position

  # chunk: Radios, checkboxes and name-link rows reach 44 px
  @todo
  Scenario: No small targets on touch widths
    Given Today, settings and registration at 1024 and 768 touch
    When every control is measured
    Then none is under 24 px in either dimension and every radio, checkbox and patient row is at least 44 px tall as a target

  @todo
  Scenario: Walk-in is not longer
    Given the walk-in path
    When a new and a returning patient are checked in
    Then the counts are still at most 6 clicks for a new patient and 3 for a returning one

  @todo
  Scenario: Accessible in both directions
    Given the changed screens in English and Arabic
    When axe runs
    Then there is no violation

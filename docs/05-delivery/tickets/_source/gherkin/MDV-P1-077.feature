Feature: Library defaults: 44 px Input, Select and Textarea, a required Table label, and 44 px hit areas for Switch
  The library default (36 px) contradicts the house 44 px rule, so 164 call sites patch it and a missed patch gives a small control (seen in the platform console).

  # chunk: Library defaults: 44 px Input, Select and Textarea, a required Table label, and 44 px hit areas for Switch
  @todo
  Scenario: Defaults are 44 px
    Given the /__ui gallery
    When Input, Select trigger and Textarea render with no class added
    Then each is at least 44 px tall in English and in Arabic

  @todo
  Scenario: No visible change at patched sites
    Given the screens that already patched their controls to 44 px
    When screenshots before and after are compared
    Then no control changed size

  @todo
  Scenario: Unpatched controls grow, on purpose
    Given the call sites that had no patch
    When the change is reviewed
    Then the pull request lists each one and each now measures 44 px, including the platform console invite form

  @todo
  Scenario: Every table has a name
    Given any Table in web/clinic or web/platform-admin
    When it is used without a label
    Then TypeScript fails the build, and the four tables that lacked one announce a translated label to a screen reader

  @todo
  Scenario: Switch is a 44 px target
    Given a Switch on a 1024 px touch viewport
    When its hit area is measured
    Then it is at least 44 by 44 px and the visible track is unchanged

  @todo
  Scenario: Accessible and mirrored
    Given the gallery page in English and Arabic
    When axe runs
    Then there is no violation and the controls mirror in right-to-left

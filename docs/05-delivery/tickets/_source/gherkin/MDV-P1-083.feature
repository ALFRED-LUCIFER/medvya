Feature: Chip, Tag and Banner in the shared library, with tone, icon and size; the allergy has one look
  About 12 chip variants exist; three styles sit side by side on one Today row.

  # chunk: Chip, Tag and Banner in the shared library, with tone, icon and size; the allergy has one look
  @todo
  Scenario: One vocabulary
    Given /__ui
    When Chip, Tag and Banner are opened
    Then each renders in the seven tones, with and without an icon, in English and Arabic

  @todo
  Scenario: Existing looks unchanged
    Given FrontDeskTag, StatusChip and ProvenanceTag
    When captured before and after
    Then no pixel differs

  @todo
  Scenario: Words always
    Given any Chip, Tag or Banner
    When it is built without a text label
    Then TypeScript fails the build

  @todo
  Scenario: One allergy look
    Given AllergyBlock
    When it renders recorded, not recorded and no known allergies
    Then the three match the presentation the pilot doctor agreed and the same component is used by PatientBanner

  @todo
  Scenario: AI is provisional
    Given the ai tone
    When it renders
    Then it is dashed, carries the word draft and cannot be combined with a confirmed or signed label

  @todo
  Scenario: Accessible
    Given the gallery in English and Arabic
    When axe runs
    Then there is no violation and text contrast is at least 4.5:1 for every tone

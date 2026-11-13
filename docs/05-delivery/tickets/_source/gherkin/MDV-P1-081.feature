Feature: PatientBanner in the shared library: full, strip and desk variants, shown in the gallery
  Three patient headers exist and the allergy looks different in each: a red pill on the chart, plain text in the consultation.

  # chunk: PatientBanner in the shared library: full, strip and desk variants, shown in the gallery
  @todo
  Scenario: One banner, three variants
    Given /__ui
    When the PatientBanner section is opened
    Then full, strip and desk render from the same component and the desk variant contains no allergy, diagnosis, medicine or vitals text

  @todo
  Scenario: Allergy in words, never clipped
    Given a patient with five recorded allergies and a long name
    When the strip renders at 1440, 1024, 768 and 375 px
    Then the first two allergies are readable in words, a count opens the rest, nothing is cut off and no ellipsis stands alone

  @todo
  Scenario: Unknown is not none
    Given the three allergy states
    When each renders
    Then Allergies not recorded is a warning block with words, No known allergies is neutral text with a tick, and neither can be produced by passing an empty list

  @todo
  Scenario: Status is not colour alone
    Given the allergy block
    When it is viewed in greyscale and read by a screen reader
    Then the word Allergy and the substance are present as text

  @todo
  Scenario: Mirrors and passes axe
    Given the gallery in English and Arabic
    When axe runs on every variant
    Then there is no violation and the layout mirrors in right-to-left

  @todo
  Scenario: Un-pins at high zoom
    Given the strip with the pinned option at 400 % zoom
    When the page scrolls
    Then the strip scrolls with the content

  @todo
  Scenario: Clinical agreement is recorded
    Given this ticket's issue
    When it is closed
    Then it holds the pilot doctor's dated agreement on the strip contents and the allergy look

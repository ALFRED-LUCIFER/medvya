Feature: Pinned patient strip and layout footer on the consultation, its sub-pages, nurse intake and the patient record
  While the doctor prescribes, nothing on screen names the patient or the allergy: the banner scrolls away on a page up to 5,350 px tall and the prescription table starts near 4,200 px.

  # chunk: Strip and footer on the consultation and its sub-pages
  @todo
  Scenario: Patient and allergy stay in view
    Given an open consultation for a patient with a recorded penicillin allergy at 1440 by 900, 1024 by 768 and 768 by 1024
    When the page is scrolled to the prescription
    Then the patient's name and the words Allergy: Penicillin are inside the viewport

  @todo
  Scenario: The same on the sub-pages and nurse intake
    Given the medicines and problems sub-pages and nurse intake at 1024 touch
    When each is scrolled to its last field
    Then the strip is still visible and names the same patient

  @todo
  Scenario: The pin is proven
    Given the browser test for the strip
    When the pinning is removed from the component
    Then the test fails (mutation check, shown in the pull request)

  @todo
  Scenario: Focus is never under pinned chrome
    Given the consultation
    When every control is reached with Tab
    Then each focused element is fully visible and none is under the strip or the footer

  @todo
  Scenario: One banner everywhere
    Given the codebase
    When it is searched
    Then encounters IdentityBanner and the primitive IdentityBanner are gone and the chart and the consultation show the allergy with the same look

  # chunk: Nurse intake, patient record and desk record use the one banner
  @todo
  Scenario: The desk sees no clinical data
    Given a receptionist on the desk record
    When the header renders
    Then it shows no allergy, diagnosis, medicine or vitals, and the server's refusal of clinical routes for that role is unchanged

  @todo
  Scenario: Zoom and direction
    Given 400 % zoom
    When the page scrolls
    Then strip and footer scroll with it; and in Arabic the strip mirrors and axe finds no violation in either language

  @todo
  Scenario: Pinned chrome budget
    Given 1024 by 768
    When top bar, strip, section anchors and footer are measured
    Then together they take at most 30 % of the height

Feature: Addenda, version history and print/PDF of the signed note (E-06)
  As a doctor, I want to amend signed notes by addendum and print them, so that corrections are visible and the original is never altered.

  # chunk: Add addenda and version history
  @todo
  Scenario: Addendum never alters v1
    Given a signed note v1
    When an addendum is signed
    Then v1 is unchanged and viewing shows the original plus addenda in order
  @todo
  Scenario: Addendum needs reason and step-up
    Given an addendum
    When the doctor tries to sign without a reason or step-up authentication
    Then it is refused

  # chunk: Add print/PDF of the signed note
  @todo
  Scenario: Server-side PDF with hash
    Given a signed note
    When the PDF is generated
    Then it renders server-side with MRN and DOB on every page and is stored with a hash
  @todo
  Scenario: Print view accessibility
    Given the print view
    When checked with a screen reader and at 200 % zoom
    Then it passes

Feature: Patient 360 (E-01): banner with identity and allergies always visible; problems, medicines, vitals trends
  As a doctor or nurse, I want one verified chart view with identity and allergies always visible, so that I treat the right patient safely.

  # chunk: Add the identity banner and allergy states
  @todo
  Scenario: Allergy shown with icon and text
    Given a patient with a verified penicillin allergy
    When Patient 360 is opened
    Then the banner shows "Allergy: Penicillin · verified" with icon and text
  @todo
  Scenario: Distinct allergy states
    Given patients with "No known allergies" and "Allergies not recorded"
    When their banners are shown
    Then the two states are visibly distinct

  # chunk: Add provenance tags on every chart fact
  @todo
  Scenario Outline: Provenance tag on chart facts
    Given a chart fact whose source is <source>
    When Patient 360 shows it
    Then it carries the tag <source>
    Examples:
      | source |
      | Clinician verified |
      | Patient reported |
      | Imported |
      | Synced from HIS |
  @todo
  Scenario: No unlabelled value
    Given any chart value
    When it is displayed
    Then it always has a provenance tag

  # chunk: Add access audit and receptionist block
  @todo
  Scenario: Opening Patient 360 is audited
    Given a clinician with a purpose
    When they open Patient 360
    Then an audit event with the purpose is written
  @todo
  Scenario: Receptionist is refused
    Given a receptionist
    When they load Patient 360
    Then the response is 403 and the attempt is audited

  # chunk: Match desktop and tablet layouts and zoom
  @todo
  Scenario: Layout and zoom
    Given the desktop and tablet designs
    When Patient 360 is viewed at normal size and at 200 % zoom
    Then the layout matches the designs and stays usable

Feature: Audit log: every action has a plain name; the default view is chart opens and changes
  The action filter lists Appointment booked beside encounter.end, rx.check and patient.chart.allergy-flag; one load of Today writes a row per patient.

  # chunk: Audit log: every action has a plain name; the default view is chart opens and changes
  @todo
  Scenario: No codes on screen
    Given the audit log and its filter
    When every row and option is read
    Then none shows a dotted developer code

  @todo
  Scenario: A new action must be named
    Given a new audit action with no translation
    When the unit test runs
    Then it fails

  @todo
  Scenario: Default answers the owner's question
    Given the log opened fresh
    When the rows are read
    Then they are chart opens and changes, and All activity shows the rest

  @todo
  Scenario: Nothing is hidden for good
    Given the export
    When it is downloaded
    Then it holds every row regardless of the view

  @todo
  Scenario: Three languages and axe
    Given English, Hindi and Arabic
    When the log renders
    Then names are translated and axe finds no violation in English and Arabic

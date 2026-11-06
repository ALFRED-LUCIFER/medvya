Feature: Vaccinations and recalls: child, travel and adult schedules with reminders (E-08)
  As a doctor or nurse, I want vaccination schedules, dose records and recalls, so that patients stay up to date.

  # chunk: Gate on clinical scope approval
  @todo
  Scenario: Built only if clinically approved
    Given the vaccination feature
    When work is planned
    Then it proceeds only if the intake record approves the clinical scope

  # chunk: Show schedule against doses given
  @todo
  Scenario Outline: Schedule shows status per vaccine
    Given a patient with a vaccination schedule
    When the nurse opens E-08
    Then a dose with status "<status>" is shown with age, vaccine and date

    Examples:
      | status               |
      | Given                |
      | Reported by guardian |
      | Due                  |
      | Upcoming             |

  # chunk: Record a dose given
  @todo
  Scenario: Dose record captures required details and next due
    Given a nurse records a dose
    When they enter vaccine, batch or lot, expiry, site and route, and giver
    Then the record is saved
    And the next due date is computed per schedule

  # chunk: Send recall reminders
  @todo
  Scenario: Recall reminders respect consent and hide clinical detail
    Given a vaccination is due
    When a recall reminder is sent
    Then it is sent only with consent
    And it contains no clinical details

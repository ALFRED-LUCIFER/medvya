Feature: Schedule templates: working hours, breaks, slot length per service, per-location calendars, time zones
  As a practice admin, I want weekly schedule templates per doctor and location, so that the calendar offers bookable slots exactly when the doctor works.

  # chunk: Add schedule template slot generation
  @done
  Scenario: Template generates slots in facility local time
    Given a template Mon-Fri 09:00-13:00 and 17:00-20:00 with 20-minute slots
    When the calendar is shown
    Then bookable slots appear exactly in those windows in facility local time

  @done
  Scenario: Breaks and closed days show as Unavailable
    Given a break or a closed day
    Then it shows as Unavailable (hatched) with the reason
    And it cannot be booked

  @done
  Scenario: Changing a template lists affected appointments
    Given booked appointments exist
    When the template changes
    Then only future unbooked slots are affected
    And existing appointments outside the new hours are listed for action

  # chunk: Add multi-slot visit types and time-zone handling
  @done
  Scenario: Visit types can span multiple slots
    Given a visit type longer than one slot
    Then the calendar renders it spanning those slots

  @done
  Scenario Outline: Time zones are handled correctly
    Given a facility in <zone>
    When slots are generated
    Then times are correct (unit tests)
    Examples:
      | zone                                   |
      | IST (no daylight saving)               |
      | GST (no daylight saving)               |
      | a test zone with daylight saving       |

  # chunk: Add template editing UI in Settings Hours
  @todo
  Scenario: Admin edits the weekly template in Settings -> Hours
    Given an admin opens Settings -> Hours
    When they edit working hours, breaks and slot length and save
    Then the template is updated

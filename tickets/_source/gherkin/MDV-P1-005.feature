Feature: Practice settings: profile, locations, operating hours and holidays (F-08)
  As a practice admin, I want to manage my clinic profile, locations, hours and holidays, so that the practice runs on accurate settings.

  # chunk: Add settings access control and audit trail
  @done
  Scenario Outline: Only owners and admins edit profile and locations
    When a user with role <role> edits the profile or locations
    Then the server responds <result>
    Examples:
      | role          | result |
      | owner         | allowed |
      | admin         | allowed |
      | other role    | 403    |

  @done
  Scenario: Settings changes are audited
    When any settings change is saved
    Then the audit log records the change with before and after values and no PHI

  # chunk: Add holiday closures that list appointments to move
  @done
  Scenario: Holiday blocks bookings and lists appointments to move
    Given an existing appointment on a date
    When a holiday is added for that date and location
    Then new bookings on that date for the location are blocked
    And the existing appointments that need moving are listed

  # chunk: Add secure logo upload
  @todo
  Scenario: Logo upload is validated, scanned and served privately
    When an admin uploads a logo
    Then its type and size are validated
    And it is malware-scanned
    And it is stored in private Blob and served via a time-limited link

  # chunk: Localise settings with RTL
  @done
  Scenario Outline: Settings work in each language
    When a user opens settings in <language>
    Then the settings are translated and RTL layout applies for Arabic
    Examples:
      | language |
      | English  |
      | Hindi    |
      | Arabic   |

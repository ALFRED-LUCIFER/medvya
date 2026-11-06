Feature: Notification centre for staff (D-03), push-safe content
  As a staff member, I want an in-app notification centre, so that I see important updates without exposing patient data.

  # chunk: Add notification centre overlay
  @todo
  Scenario: Notifications are scoped
    Then notifications are scoped to the user's tenant, facility and role

  @todo
  Scenario: Push content is generic
    When a push or browser notification is sent
    Then it never includes patient names or results
    And its content is "New update from {clinic}. Open Medvya to view." (payload test)

  @todo
  Scenario: Read state persists across devices
    When a user marks all or one item read
    Then the read state persists across devices

  @todo
  Scenario: Overlay is accessible
    Then the overlay is keyboard accessible and screen-reader friendly

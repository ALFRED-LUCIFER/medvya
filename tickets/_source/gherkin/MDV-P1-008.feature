Feature: Add staff with role, facility scope, permissions and invite; custom roles (F-10)
  As a practice admin, I want to add staff with roles and permissions, so that each person has only the access they need.

  # chunk: Add staff wizard and invite
  @todo
  Scenario: Choosing Doctor redirects to Add doctor
    When the admin chooses the role "Doctor"
    Then "Doctors need licence verification. Use Settings → Doctors → Add doctor." is shown
    And no staff membership is created

  @todo
  Scenario: Invite uses the P0 flow and table shows states
    When the admin invites a staff member
    Then the P0 invitation flow is used
    And the staff table shows invited, active, suspended or offboarded status and MFA enrolment state

  # chunk: Add locked permissions and custom roles
  @todo
  Scenario: Locked permissions are not editable
    Given a built-in role has locked permissions
    Then they are displayed but cannot be granted or removed
    And custom roles cannot exceed the capabilities of practice admin

  # chunk: Audit permission changes and prove receptionist limits
  @todo
  Scenario: Permission changes are audited
    When any permission changes
    Then the change is written to the audit log

  @todo
  Scenario: Receptionist cannot open clinical endpoints
    Given a receptionist created through this flow
    When they call a clinical endpoint
    Then access is refused (P0 security test reused)

Feature: Organisation, facilities and departments administration (H-02)
  As hospital IT, I want to administer the facility tree and departments, so that permissions follow the right facility.

  # chunk: Administer facilities and departments with facility-scoped permissions
  @todo
  Scenario: Facility scope drives every permission check
    Given users from different facilities
    When each tries to reach data of the other facility
    Then access is denied (cross-facility tests)

  @todo
  Scenario: Departments are assignable to staff and encounters
    Given a facility with departments
    When hospital IT assigns a department to a staff member and to an encounter
    Then both assignments are saved and shown

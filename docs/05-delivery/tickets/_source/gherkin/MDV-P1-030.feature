Feature: Clinic overview: multi-doctor Today and shared waiting room (D-04)
  As a receptionist, I want a clinic-wide overview and the ability to reassign waiting patients, so that load is balanced between doctors.

  # chunk: Add clinic overview and reassignment
  @done
  Scenario: Overview lists every working doctor
    Then the overview lists every doctor working today with counts per state and a current delay estimate

  @done
  Scenario: Reassigning a waiting patient is atomic and audited
    When a waiting patient is reassigned to another doctor
    Then the appointment moves to the other doctor's queue atomically
    And it is audited

  @done
  Scenario: Doctors see their own schedule read-only
    When a doctor opens their view
    Then they see their own schedule read-only with their next patients

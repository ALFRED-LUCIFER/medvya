Feature: Patient registration - UAE flow (Emirates ID manual entry, Arabic name RTL, emirate, insurance card)
  As a receptionist, I want to register a UAE patient, so that Emirates ID, Arabic name and insurance are captured correctly.

  # chunk: Add UAE registration rules and Arabic name
  @done
  Scenario: Minimum to register in the UAE
    When a receptionist enters name, DOB, sex, mobile and Emirates ID or passport
    Then registration is accepted

  @done
  Scenario: Emirates ID format and expiry
    When an invalid Emirates ID is entered
    Then "Format 784-YYYY-NNNNNNN-N" is shown
    And an expired Emirates ID shows a warning but never blocks registration

  @done
  Scenario: Arabic name displays correctly
    When an Arabic name is typed right to left
    Then it displays correctly in the preview card and review step

  # chunk: Show HIE label on the consents step
  @todo
  Scenario: Emirate determines the HIE label in consents
    Given the selected emirate is one of the UAE emirates
    Then the consents step shows the health information exchange label for that emirate (Malaffi, NABIDH or Riayati)

  # chunk: Guard UAE tenant creation to the UAE deployment
  @todo
  Scenario: UAE tenant cannot be created in the India deployment
    Given the India deployment configuration
    When a UAE tenant is created
    Then it is refused (configuration test)

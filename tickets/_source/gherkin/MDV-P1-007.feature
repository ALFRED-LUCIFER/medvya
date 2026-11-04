Feature: Add doctor (clinic mode): details, licence India/UAE, verification, schedule link, fees, access and signature image (F-09)
  As a practice admin, I want to add a doctor with licence, fees, access and signature, so that only verified doctors can record and sign.

  # chunk: Add doctor wizard with licence validation
  @todo
  Scenario: Licence number format and expiry are validated
    When an admin enters a licence for an authority with a known format
    Then the number is validated against that format
    And the expiry date is required

  # chunk: Enforce licence verification server-side
  @todo
  Scenario: Doctor cannot record or sign until licence is Verified
    Given a doctor's licence status is not Verified
    When the doctor tries to record or sign
    Then the server refuses the action

  # chunk: Add verification decision audit and rejection notice
  @todo
  Scenario: Verification decision records who and when
    When a licence is verified or rejected
    Then who decided and when are recorded and audited

  @todo
  Scenario: Rejected licence notifies the owner with the reason
    When a licence is rejected
    Then the owner is notified with the reason

  # chunk: Add private signature image
  @todo
  Scenario: Signature image is private and printed only
    When a doctor's signature image is uploaded
    Then it is stored privately and appears only on printed prescriptions
    And the UI states "Signing in Medvya is always an authenticated action by the doctor (passkey / MFA), never the image alone."

  # chunk: Show new doctor in schedule columns
  @todo
  Scenario: New doctor appears in schedule once hours are set
    Given a new doctor has been added
    When their hours are set
    Then they appear in the schedule columns

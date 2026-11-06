Feature: Share Rx with patient/pharmacy (e-Rx PDF/link) and pharmacy dispatch record (E-10)
  As a doctor or receptionist, I want to securely share an authorised Rx and record dispatch, so that patients and pharmacies receive it safely.

  # chunk: Add secure sharing and dispatch record
  @todo
  Scenario: Share links are protected and audited
    Given a shared Rx link
    When opened
    Then it expires, requires OTP to the patient's registered mobile, and the open is audited
  @todo
  Scenario: Message text has no clinical detail
    Given a share message
    When sent
    Then it contains no medicine names or clinical details
  @todo
  Scenario: Dispatch changes recorded with actor
    Given a pharmacy dispatch
    When its state changes
    Then the change is recorded with actor
  @todo
  Scenario: No regulator submission in P2
    Given the P2 API
    When endpoints are listed
    Then no regulator submission endpoint exists (scope guard)

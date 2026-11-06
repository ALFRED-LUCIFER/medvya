Feature: Subscription billing in the console and for owners: invoices, payment method, add seats, dunning ladder, close practice
  As practice owner, I want this delivered, so that the 6 Nov 2026 design works for a real practice.

  # chunk: Subscription billing in the console and for owners: invoices, payment 
  @todo
  Scenario: Check 1
    Given a payment fails
    When day 30 is reached
    Then online bookings pause and clinicians can still read and treat
  @todo
  Scenario: Check 2
    Given a non-owner
    When opens billing
    Then the API returns 403
  @todo
  Scenario: Check 3
    Given a practice is closed
    When the owner confirms
    Then a full export is offered first and the plan stops at the end of the paid period

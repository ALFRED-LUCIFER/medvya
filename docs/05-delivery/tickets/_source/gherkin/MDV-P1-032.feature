Feature: Email confirmations and 24 h / 2 h reminders via SendGrid and Hangfire
  As a patient, I want email confirmations and reminders, so that I remember my appointment.

  # chunk: Add booking confirmation and timed reminders
  @done
  Scenario: Confirmation and reminders are sent on time
    When a booking is made
    Then a confirmation is sent within 1 minute
    And reminders are sent 24 h and 2 h before in facility time
    And quiet hours and channel consent are respected

  # chunk: Cancel reminders on change with idempotent messages
  @done
  Scenario: Reschedule or cancel cancels pending reminders
    When an appointment is rescheduled or cancelled
    Then pending reminders are cancelled
    And the right message is sent once (idempotency test)

  # chunk: Add delivery status and retries
  @done
  Scenario: Delivery status is visible to reception
    Then each appointment shows delivery status: queued, sent, delivered, failed or bounced

  @done
  Scenario: Failed sends retry then show as failed
    When a send fails
    Then it is retried with backoff up to the configured limit
    And then shown as failed on the board and notification centre

  # chunk: Secure the Hangfire dashboard and keep infrastructure lean
  @done
  Scenario: Hangfire dashboard is operations-only and no Redis or Service Bus exist
    Then the Hangfire dashboard is reachable only by Medvya operations through an authenticated admin route
    And no Redis or Service Bus resources exist in P1 infrastructure

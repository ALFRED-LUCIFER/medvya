Feature: Real-time board updates, call/room patient, nonclinical statuses (SignalR)
  As a receptionist, I want board changes to appear live on every device, so that the whole desk sees the same queue.

  # chunk: Add real-time board hub
  @done
  Scenario: State change reaches another device within 2 seconds
    When a state changes on one device
    Then it appears on another device's board within 2 seconds

  @done
  Scenario: Hub is authorised per facility
    When a user tries to subscribe to another facility's board
    Then access is refused (security test)

  @done
  Scenario: Board resyncs after a network drop
    When the network drops and returns
    Then the board resyncs without duplicates or lost moves

  @done
  Scenario: Messages carry nonclinical fields only
    Then hub messages carry ids and nonclinical fields only

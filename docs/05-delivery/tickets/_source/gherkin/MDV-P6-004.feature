Feature: Device health, battery, encrypted offline buffer, upload conflicts and staged firmware updates (W-02)
  As a doctor or practice admin, I want to see device health and keep recordings safe offline, so that no recording is lost and firmware stays trustworthy.

  # chunk: Show device health and the offline queue
  @todo
  Scenario: Device page shows health and queue
    Given a paired clip with recordings waiting
    When the doctor opens the W-02 device page
    Then battery %, BLE status and firmware version are shown
    And the queue shows "2 recordings in encrypted queue" with a Sync now action

  @todo
  Scenario: Offline recordings stay encrypted and sync without loss
    Given the clip recorded while offline
    When the recordings sit on the device and later sync
    Then they stay encrypted on the device
    And none are lost (offline suite, exit gate 1)

  # chunk: Accept only signed firmware in staged updates
  @todo
  Scenario: Signed image is installed in a staged update
    Given a signed firmware image 1.5.0 is available for a clip on 1.4.2
    When the staged update runs
    Then the clip updates to 1.5.0
    And anti-rollback prevents installing an older image

  @todo
  Scenario: Tampered image is rejected
    Given a firmware image with an invalid signature
    When the clip receives it
    Then the image is rejected

  # chunk: Surface upload conflicts and verify battery life
  @todo
  Scenario: Upload conflict keeps both recordings and asks the clinician
    Given the same session was uploaded from the phone and from the clip
    When the conflict is detected
    Then both recordings are kept
    And the clinician is asked to choose
    And nothing is merged silently

  @todo
  Scenario: Battery lasts a full clinic day
    Given a clip in a field test with mixed use
    When 8 hours have passed
    Then the battery still lasts (at least 8 h, exit gate 2)

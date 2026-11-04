Feature: Encrypted chunked upload with resume; upload progress, failed/partial recovery states (A-06)
  As a doctor, I want resilient encrypted upload with resume, so that recordings are not lost on poor networks.

  # chunk: Add resumable chunk upload
  @todo
  Scenario: Resume without duplicates
    Given the network is killed mid-upload and restored
    When upload resumes
    Then committed chunks are not re-sent and there are no duplicates (test)
  @todo
  Scenario: Integrity mismatch rejected
    Given a chunk whose hash does not match
    When it is committed
    Then it is rejected and re-upload is requested

  # chunk: Add scoped upload tokens
  @todo
  Scenario: SAS limited to session prefix
    Given an upload SAS token
    When used outside its session prefix or after expiry
    Then writing is refused, and it expires within minutes

  # chunk: Add partial recovery and device cleanup
  @todo
  Scenario: Partial audio marks missing range
    Given a failed upload with missing chunks
    When the doctor continues with partial audio
    Then the missing range is marked in the transcript and every affected draft section
  @todo
  Scenario: Device buffer cleared
    Given a successful upload
    When complete
    Then no audio is retained on the device

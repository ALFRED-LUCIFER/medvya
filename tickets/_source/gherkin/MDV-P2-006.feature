Feature: Documents: upload, malware scan, secure viewer with time-limited links, scan to patient file (E-03)
  As a doctor, nurse or receptionist, I want to upload and securely view patient documents, so that scanned records join the patient file safely.

  # chunk: Add upload with malware scan and quarantine
  @todo
  Scenario: Not viewable until scan passes
    Given an uploaded file whose scan has not passed
    When anyone tries to view it
    Then it is not viewable
    And an infected file is quarantined and the uploader is told
  @todo
  Scenario: Original files are immutable
    Given a stored document
    When its type or date is edited
    Then the original file is unchanged and the metadata edit is audited

  # chunk: Add the secure time-limited viewer
  @todo
  Scenario: Viewer links expire and are bound to the user
    Given a clinician opens a document
    When a viewer link is issued
    Then it expires within 5 minutes and is bound to the requesting user
    And blob names contain no PHI
  @todo
  Scenario: Receptionist upload only
    Given a receptionist
    When they use documents
    Then they can upload and see the list but cannot open clinical documents (policy test)

  # chunk: Add phone camera scan to patient file
  @todo
  Scenario: Multi-page camera capture
    Given a phone on mobile web
    When the user captures several pages with the camera
    Then they are combined into a single PDF in the patient file

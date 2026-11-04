Feature: Lab feeds (HL7 v2 ORU/ORM over MLLP/VPN) and imaging viewer links (N-15)
  As a doctor or hospital IT, I want lab orders, results and imaging links connected, so that results reach the right patient without copying images.

  # chunk: Resolve the N-15 design and lab vendor specifics
  @todo
  Scenario: N-15 design and lab specifics are agreed
    Given N-15 needs design and the lab vendor specifics are unknown
    When the design and vendor interface details are agreed
    Then they are recorded before build starts

  # chunk: Send orders and receive results
  @todo
  Scenario: ORU results create matched inbox items
    Given an ORU message for an order
    When it is received
    Then an inbox item is created and matched to the order
    And a low-confidence match still needs a human

  @todo
  Scenario: Orders are sent out as ORM
    Given a clinician places a lab order
    When it is sent
    Then an ORM^O01 message is sent

  # chunk: Handle ACK, NACK and retries visibly
  @todo
  Scenario: ACK, NACK and retries are visible in H-01
    Given messages are sent over MLLP/VPN
    When an ACK or NACK arrives or a retry happens
    Then it is visible in H-01

  # chunk: Open imaging by link only
  @todo
  Scenario: Imaging opens in the provider viewer via link only
    Given a radiology report as text with a DICOMweb link
    When the clinician opens the imaging
    Then the provider viewer opens via the link
    And no image copy is stored in Medvya

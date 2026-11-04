Feature: Apply for payment gateway merchant accounts and WhatsApp Business template approvals
  As the founder, I want merchant and WhatsApp approvals in place, so that payments and messaging can go live.

  # chunk: Obtain merchant and WhatsApp approvals
  @todo
  Scenario: Merchant accounts approved
    Given the merchant applications
    When payments go live
    Then accounts are approved, or the cash plus manual UPI QR fallback is documented
  @todo
  Scenario: Templates approved
    Given the WhatsApp templates
    When checked
    Then they are approved in three languages, or the SMS fallback is documented

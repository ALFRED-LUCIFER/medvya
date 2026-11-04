Feature: Feature intake: adoption target, evidence review and regulatory classification check for every P8 feature
  As a founder or clinical advisor, I want an intake record before any P8 feature, so that only evidenced features are built.

  # chunk: Require an approved intake record and release-notes template
  @todo
  Scenario: Each P8 ticket links an approved intake record
    Given a P8 feature ticket
    When work is about to start
    Then it links an approved intake record with an adoption target, evidence review and regulatory classification review for any decision-support aspect

  @todo
  Scenario: Release-notes template exists and is used
    Given a P8 feature is released as a minor version
    When the release notes are written
    Then the minor-version template is used

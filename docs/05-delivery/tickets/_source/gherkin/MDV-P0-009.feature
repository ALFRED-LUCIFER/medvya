Feature: Database security baseline part 2: national/insurance ID encryption, masked access, storage encryption with a customer-managed key
  As a developer, I want national and insurance IDs encrypted and contact data masked, so that admins and support never see raw sensitive values.

  # chunk: Encrypt national and insurance IDs (envelope encryption, ADR-012)
  @done
  Scenario: Admin sees ciphertext only
    When a DBA or admin queries an encrypted column without access to the column master key
    Then they see ciphertext only

  @done
  Scenario Outline: API looks up a patient by exact identifier
    Given a patient stored with an <identifier>
    When the API looks the patient up by exact <identifier> through EF Core using a test key
    Then the patient is found

    Examples:
      | identifier   |
      | Emirates ID  |
      | ABHA number  |

  @done
  Scenario: Partial-match lookup on a deterministic column is rejected
    When a LIKE query runs against a deterministic encrypted column without an enclave
    Then it is rejected

  # chunk: Mask contact data and document the protected columns
  @done
  Scenario: Support role sees masked values
    When a login in the support role reads phone, email or date of birth
    Then it sees masked values

  @done
  Scenario: Protected columns are documented and kept small
    Then the protected-column list is in the data classification register (MDV-P0-016)
    And it is kept deliberately small

  # chunk: Switch TDE to a customer-managed key with rotation
  @todo
  Scenario: TDE protector is a rotating Key Vault key
    Then the TDE protector is a Key Vault key with a rotation policy
    And backups inherit it

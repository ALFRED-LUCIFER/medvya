Feature: Multilingual notes: Arabic, English, Hindi and Malayalam incl. mixed-language consultations
  As a doctor, I want transcription and drafting in my patients' languages, including mixed speech, so that notes are accurate for every consultation.

  # chunk: Extend transcription and drafting to Arabic and Malayalam
  @todo
  Scenario Outline: Mixed-language consultation is transcribed and drafted
    Given a consultation spoken in <languages>
    When the transcript and draft note are produced
    Then the draft is an AI draft until a licensed clinician accepts each field and signs

    Examples:
      | languages  |
      | Arabic     |
      | Malayalam  |
      | Arabic and English |
      | Malayalam and English |
      | Hindi and English |

  # chunk: Show original beside translation
  @todo
  Scenario Outline: Original text is shown beside the translation
    Given a transcript in <language>
    When the doctor views the transcript and the evidence
    Then the original-language text is shown beside the translation

    Examples:
      | language  |
      | Arabic    |
      | English   |
      | Hindi     |
      | Malayalam |

  # chunk: Gate each language on evaluation results
  @todo
  Scenario: Each language meets the same safety gate
    Given a per-language evaluation set
    When the evaluation runs
    Then there are 0 fabricated medicines or doses

  @todo
  Scenario: Language quality is documented before tenant enablement
    Given a language has not been enabled for a tenant
    When an admin tries to enable it without documented quality results
    Then enabling is not allowed until results are documented

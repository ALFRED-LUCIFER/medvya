Feature: Transcription with diarisation and language identification (in-region Azure AI Speech)
  As the system, I want to transcribe and diarise completed recordings in-region, so that drafting has an authoritative transcript.

  # chunk: Add batch transcription with diarisation
  @todo
  Scenario: Synthetic consultation transcribed
    Given a 10-minute synthetic consultation
    When transcribed
    Then segments, timestamps, language tags, translations and provisional speakers are produced
  @todo
  Scenario: Low confidence surfaced
    Given low-confidence segments
    When review opens
    Then they are marked and surfaced

  # chunk: Protect and time the transcript
  @todo
  Scenario: Encrypted, not logged
    Given segment content
    When stored and processed
    Then it is encrypted and no transcript text appears in logs or telemetry
  @todo
  Scenario: Transcription latency budget
    Given a 10-minute visit
    When transcribed
    Then transcription contributes 40 s or less at p50, leaving budget for drafting (overall 60 s p50 measured in MDV-P3-025)

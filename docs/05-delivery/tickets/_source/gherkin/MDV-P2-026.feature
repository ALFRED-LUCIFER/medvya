Feature: Doctor and nurse training (30 min each) and R2 materials
  As a founder, I want doctors and nurses trained, so that R2 is used safely.

  # chunk: Deliver training and reference cards
  @todo
  Scenario: Training attendance recorded
    Given all pilot doctors and nurses
    When training is delivered
    Then attendance is recorded
  @todo
  Scenario: Quick reference cards present
    Given each workstation
    When checked
    Then a quick reference card is available

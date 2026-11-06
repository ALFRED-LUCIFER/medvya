Feature: Medvya design system in code: shadcn/ui + Tailwind tokens and clinical status primitives (packages/ui)
  As any user, I want a consistent, accessible interface, so that clinical status is always clear.

  # chunk: Add design tokens and button/card styling
  @done
  Scenario: Tokens match the design documents
    Then tokens in packages/ui/src/styles.css (palette, Manrope/JetBrains Mono, radius 10, breakpoints 360/768/1280/1600) match docs/02-design/tokens.md
    And WCAG deviations are listed in docs/02-design/README.md

  @done
  Scenario: Buttons and cards follow the style
    Then buttons are at least 44px, bold, not uppercase
    And cards have a 1px #DFE7ED border and no shadow

  # chunk: Add clinical primitives with all states
  @done
  Scenario: Every primitive renders all its states
    Then every primitive renders all states listed in Handoff section 1

  @done
  Scenario: Status is never colour alone
    Then no status is conveyed by colour alone
    And icon and text are present, checked by test

  # chunk: Verify focus, contrast, axe and reduced motion
  @done
  Scenario: Focus ring is visible on interactive primitives
    Then the focus ring is 2px #2563EB with 2px offset on all interactive primitives

  @done
  Scenario: Gallery is accessible with enough contrast
    When axe runs on the gallery page
    Then it reports no violations
    And text contrast of tokens matches the Foundations ratios (at least 4.5:1 for text)

  @done
  Scenario: Reduced motion stops animation
    Given prefers-reduced-motion is set
    Then animation in RecordingPill and progress is disabled

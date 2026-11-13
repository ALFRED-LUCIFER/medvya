Feature: Fix the Expo bundling error, then run the same UX audit on the mobile apps in a simulator
  pnpm dev:clinician then opening in Expo Go fails with Cannot find module @babel/helper-define-polyfill-provider, so nobody can run or review the mobile apps.

  # chunk: Fix the Expo bundling error, then run the same UX audit on the mobile apps in a simulator
  @todo
  Scenario: Both apps start
    Given a clean checkout
    When pnpm dev:clinician and the patient app are opened in Expo Go on a simulator
    Then each bundles and shows its first screen

  @todo
  Scenario: Audit is written
    Given the running apps
    When the audit is done
    Then its findings are tickets with evidence and synthetic data only

  @todo
  Scenario: Native contrast
    Given a secondary button and a placeholder in ui-native
    When contrast is computed
    Then the border is at least 3:1 and the placeholder at least 4.5:1

  @todo
  Scenario: Dependencies stay clean
    Given the change
    When pnpm audit runs
    Then it reports no high or critical issue and the lockfile pins the helper

  @todo
  Scenario: CI covers it
    Given the mobile workspace
    When CI runs
    Then a bundle step fails if the apps stop bundling

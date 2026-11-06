# Synthetic tenant on the Azure dev environment (MDV-P0-027)

The synthetic data generator (`tools/synthetic-data-generator`) seeds local and CI databases only. In Azure the database is private (MDV-P0-038), and generated users carry the development identity provider, so they could never sign in through Entra. The Azure tenant is made through the product instead, the same way a pilot clinic is. This also walks journey J1 step 1 on the real stack.

Synthetic data only (AGENTS.md 1.1): every name below comes from `docs/00-overview/personas.md` and ends in "(synthetic)" where the form allows it. Never type a real person's details, phone number or national ID.

## Before you start

1. The dev environment is deployed and the deploy smoke test passed (MDV-P0-007, MDV-P0-042).
2. The two Entra settings in `infra/bicep/README.md` ("Manual Entra settings") are in place: MFA enforced and the `amr` claim.
3. One Entra account per persona in the dev tenant, for example `arjun.rao.synthetic@<tenant>.onmicrosoft.com`, each enrolled in MFA. Keep the passwords out of the repo; share them out of band.

## Steps

| # | Who signs in | Where | What to do |
|---|---|---|---|
| 1 | Dr. Arjun Rao | `/app/signup` | Sign up, then run onboarding: clinic "Iyer Family Clinic (synthetic)", facility Adyar, Chennai, country India, licence authority TNMC. Use a licence number from the test fixtures, never a real one. |
| 2 | Dr. Arjun Rao | Practice settings → Staff | Invite A. Joseph (Receptionist), S. Thomas RN (Nurse), Priya N. (Care coordinator), K. Lakshmi (Practice admin), each to their own Entra address. |
| 3 | Each invitee | The invitation email | Open the link, sign in with their Entra account and MFA, and accept. The membership becomes active. |
| 4 | A. Joseph | Patients → Register | Register Meera Iyer (54, female) and Deepak Nair (47, male) with the India minimum. Leave ABHA empty. |
| 5 | Each persona | `/app/` | Sign in once and check the role's navigation (receptionist sees Practice items only, doctor sees Clinical and Practice). |

Roles outside a clinic pilot (hospital doctor, hospital IT, patient) are not created here.

## Record

List the dev URL and the persona sign-in addresses (not the passwords) in the R0 delivery package (MDV-P0-029).

| Date (UTC) | Done by | Personas able to sign in with MFA | Notes |
|---|---|---|---|
| _pending: first run after the dev deployment_ | | | |

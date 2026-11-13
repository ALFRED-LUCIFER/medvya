import * as React from "react"
import { CircleAlert, CircleCheck, CircleHelp, Check, TriangleAlert } from "lucide-react"
import { ltr, useI18n } from "@medvya/i18n"
import { cn } from "#lib/utils"
import { Button } from "#components/ui/button"
import { Dialog, DialogContent, DialogTitle, DialogTrigger } from "#components/ui/dialog"

export type BannerAllergy = { substance: string; reaction?: string }
/** Three states that never look alike (unknown is not none). "noneKnown" must be said explicitly: an empty list is not it. */
export type AllergyState =
  | { state: "notRecorded" }
  | { state: "noneKnown" }
  | { state: "recorded"; allergies: readonly [BannerAllergy, ...BannerAllergy[]] }

export type BannerIdentity = { name: string; age: number; sex: "Female" | "Male" | "Other"; mrn: string; /** formatted by the screen */ dob: string }
export type BannerClinical = BannerIdentity & {
  allergy: AllergyState
  /** Treatment consent, worded as the consultation words it today. */
  consent?: "given" | "notRecorded"
  lifeStage?: string
  careTeam?: string
}
/** The desk variant's patient: identity, and the type refuses every clinical field (what a receptionist sees must not grow). */
export type BannerDeskPatient = BannerIdentity & { [K in Exclude<keyof BannerClinical, keyof BannerIdentity>]?: never }

type Field = "ageSex" | "mrn" | "mrnMasked" | "dob" | "lifeStage" | "allergy" | "consent" | "careTeam" | "token" | "room"

// Proposal awaiting the pilot doctor and nurse (validation plan questions 3 and 4): their answer is an edit to these lists and this number.
/** What each variant shows after the name, in order. desk holds no clinical field. */
export const bannerFields = {
  full: ["ageSex", "mrn", "dob", "lifeStage", "allergy", "consent", "careTeam"],
  strip: ["ageSex", "mrn", "dob", "allergy", "consent", "token", "room"],
  desk: ["ageSex", "mrnMasked", "dob"],
} as const satisfies Record<string, readonly Field[]>
/** Recorded allergies shown in words before the rest fold into a count that opens the full list. */
export const allergiesInWords = 2

const maskMrn = (mrn: string) => `•••• ${mrn.slice(-4)}`

function AllergyWords({ allergy }: { allergy: BannerAllergy }) {
  const { t } = useI18n()
  return (
    <span data-slot="allergy" className="inline-flex max-w-full items-center gap-1.5 rounded-sm bg-danger-text px-2 py-1 text-label text-white">
      <TriangleAlert aria-hidden className="size-4 shrink-0" />
      <span className="min-w-0 break-words">
        {allergy.reaction
          ? t("patientBanner.allergyReaction", { substance: allergy.substance, reaction: allergy.reaction })
          : t("patientBanner.allergy", { substance: allergy.substance })}
      </span>
    </span>
  )
}

/**
 * The one allergy presentation (MDV-P1-081; proposal awaiting the pilot doctor, question 4): recorded allergies as a solid danger block
 * with an icon and the words, the first two in words and a count that opens the full list; not recorded as a warning block; no known
 * allergies as neutral text with a tick. Always words, never colour alone.
 */
function AllergyBlock({ allergy }: { allergy: AllergyState }) {
  const { t } = useI18n()
  if (allergy.state === "noneKnown")
    return <span data-slot="allergy-state" className="inline-flex items-center gap-1.5 text-label text-neutral-text"><Check aria-hidden className="size-4" />{t("banner.noAllergies")}</span>
  // A "recorded" state with no item is not an answer: it reads as not recorded, never as none.
  if (allergy.state === "notRecorded" || allergy.allergies.length === 0)
    return (
      <span data-slot="allergy-state" className="inline-flex items-center gap-1.5 rounded-sm border border-warning-border bg-warning-bg px-2 py-1 text-label text-warning-text">
        <CircleHelp aria-hidden className="size-4 shrink-0" />{t("banner.allergiesNotRecorded")}
      </span>
    )
  const rest = allergy.allergies.length - allergiesInWords
  return (
    <span className="flex max-w-full flex-wrap items-center gap-1.5">
      {allergy.allergies.slice(0, allergiesInWords).map((a, i) => <AllergyWords key={i} allergy={a} />)}
      {rest > 0 && (
        <Dialog>
          <DialogTrigger asChild>
            <Button variant="outline" className="border-danger-border text-danger-text">{t("patientBanner.moreAllergies", { count: rest })}</Button>
          </DialogTrigger>
          <DialogContent closeLabel={t("patientBanner.close")} aria-describedby={undefined}>
            <DialogTitle className="text-heading">{t("patient360.allergies")}</DialogTitle>
            <ul className="flex flex-col items-start gap-2">{allergy.allergies.map((a, i) => <li key={i}><AllergyWords allergy={a} /></li>)}</ul>
          </DialogContent>
        </Dialog>
      )}
    </span>
  )
}

type PatientBannerProps = Omit<React.ComponentProps<"section">, "children"> & (
  | { variant: "desk"; patient: BannerDeskPatient }
  | { variant: "full"; patient: BannerClinical }
  | { variant: "strip"; patient: BannerClinical; token?: React.ReactNode; room?: React.ReactNode; /** pins under the page top while it scrolls; un-pins at 400 % zoom */ pinned?: boolean }
)

/**
 * One patient banner, three variants (MDV-P1-081): full (the chart header), strip (one 56 px row, wrapping rather than clipping) and desk
 * (identity only: no allergy, diagnosis, medicine or vitals, whatever the caller passes). It only presents what the screen passes in.
 */
function PatientBanner({ variant, patient, className, ...rest }: PatientBannerProps) {
  const { t } = useI18n()
  const { token, room, pinned, ...props } = rest as { token?: React.ReactNode; room?: React.ReactNode; pinned?: boolean }
  // desk reads identity only, so clinical props a caller passes by mistake never reach the screen.
  const clinical = variant === "desk" ? undefined : (patient as BannerClinical)
  const render = (f: Field): React.ReactNode => {
    switch (f) {
      case "ageSex": return t("consult.banner.ageSex", { age: patient.age, sex: t(`patients.sex.${patient.sex}`) })
      case "mrn": return <span className="font-mono text-mono">{t("consult.banner.mrn", { mrn: ltr(patient.mrn) })}</span>
      case "mrnMasked": return <span className="font-mono text-mono">{t("consult.banner.mrn", { mrn: ltr(maskMrn(patient.mrn)) })}</span>
      case "dob": return t("consult.banner.dob", { date: `⁨${patient.dob}⁩` }) // first-strong isolate: a Latin or an Arabic date keeps its own order
      case "lifeStage": return clinical?.lifeStage
      case "careTeam": return clinical?.careTeam
      case "allergy": return clinical && <AllergyBlock allergy={clinical.allergy} />
      case "consent": return clinical?.consent && (clinical.consent === "given"
        ? <span className="inline-flex items-center gap-1.5"><CircleCheck aria-hidden className="size-4" />{t("consult.banner.consentGiven")}</span>
        : <span className="inline-flex items-center gap-1.5 text-warning-text"><CircleAlert aria-hidden className="size-4" />{t("consult.banner.consentNotRecorded")}</span>)
      case "token": return token
      case "room": return room
    }
  }
  const items = bannerFields[variant].map(f => [f, render(f)] as const).filter(([, node]) => node)
  return (
    <section
      data-slot="patient-banner"
      data-variant={variant}
      aria-label={t("banner.patient", { name: patient.name })}
      className={cn(
        "flex flex-wrap items-center gap-x-4 gap-y-1.5 border-b bg-background text-body",
        variant === "full" ? "rounded-lg border p-4" : "min-h-14 px-4 py-1.5",
        // shortcut: pinned does not yet scroll a focused element clear of the strip (StickyActionBar does, for the footer); MDV-P1-092 adds it when the strip goes on the screens.
        pinned && "sticky top-0 z-10 [@media(max-height:480px)]:static",
        className,
      )}
      {...props}
    >
      {variant === "full" && (
        <span aria-hidden className="grid size-11 shrink-0 place-items-center rounded-full bg-brand-bg text-label text-brand-text">
          {patient.name.split(" ").map(p => p[0]).slice(0, 2).join("")}
        </span>
      )}
      <h2 className="min-w-0 break-words text-heading">{patient.name}</h2>
      {items.map(([f, node]) => <span key={f} data-field={f} className="flex min-w-0 max-w-full flex-wrap items-center gap-1.5">{node}</span>)}
    </section>
  )
}

export { PatientBanner, AllergyBlock }

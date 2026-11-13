import * as React from "react"
import type { LucideIcon } from "lucide-react"
import { useI18n } from "@medvya/i18n"
import { cn } from "#lib/utils"

/** The seven tones of the token layer (MDV-P1-080). Each meaning has one colour pair; DESIGN.md "Colour and tone" says what each means. */
export type Tone = "danger" | "warning" | "success" | "info" | "brand" | "ai" | "neutral"

// Written out in full so Tailwind generates them. ai is the only dashed tone.
export const toneClasses: Record<Tone, string> = {
  danger: "border-danger-border bg-danger-bg text-danger-text",
  warning: "border-warning-border bg-warning-bg text-warning-text",
  success: "border-success-border bg-success-bg text-success-text",
  info: "border-info-border bg-info-bg text-info-text",
  brand: "border-brand-border bg-brand-bg text-brand-text",
  ai: "border-dashed border-ai-border bg-ai-bg text-ai-text",
  neutral: "border-neutral-border bg-neutral-bg text-neutral-text",
}

/**
 * Every Chip, Tag and Banner says what it means in words: the label is required. The ai tone always reads the AI draft words
 * ("AI draft · not reviewed"), so it can never carry a confirmed or signed label.
 */
export type Labelled<L extends string = "label"> =
  | ({ tone: Exclude<Tone, "ai"> } & Record<L, string>)
  | ({ tone: "ai" } & Partial<Record<L, never>>)

export type ChipProps = Omit<React.ComponentProps<"span">, "children"> & Labelled & { icon?: LucideIcon; size?: "sm" | "md" }

const chipSizes = { sm: "min-h-6 gap-1 px-2 text-caption [&>svg]:size-3.5", md: "min-h-8 gap-1.5 px-2.5 text-label [&>svg]:size-4" }

export function useLabel(tone: Tone, label: string | undefined) {
  const { t } = useI18n()
  return tone === "ai" ? t("ai.badge") : label
}

/** A state, rounded (MDV-P1-083): tone, optional icon, two sizes. Colour never stands alone: the words are always there. */
function Chip({ tone, label, icon: Icon, size = "md", className, ...props }: ChipProps) {
  const text = useLabel(tone, label)
  return (
    <span
      data-slot="chip"
      data-tone={tone}
      className={cn("inline-flex items-center whitespace-nowrap rounded-full border", toneClasses[tone], chipSizes[size], className)}
      {...props}
    >
      {Icon && <Icon aria-hidden className="shrink-0" />}
      {text}
    </span>
  )
}

export { Chip }

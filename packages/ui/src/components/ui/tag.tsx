import * as React from "react"
import type { LucideIcon } from "lucide-react"
import { cn } from "#lib/utils"
import { toneClasses, useLabel, type Labelled } from "#components/ui/chip"

export type TagProps = Omit<React.ComponentProps<"span">, "children"> & Labelled & { icon?: LucideIcon; size?: "sm" | "md" }

const tagSizes = { sm: "gap-1 px-2 py-0.5 text-caption [&>svg]:size-3.5", md: "gap-1.5 px-2 py-1 text-label [&>svg]:size-4" }

/** A category or source, square (MDV-P1-083), e.g. "From nurse intake". Same tones and label rule as Chip; wraps in narrow cards. */
function Tag({ tone, label, icon: Icon, size = "md", className, ...props }: TagProps) {
  const text = useLabel(tone, label)
  return (
    <span
      data-slot="tag"
      data-tone={tone}
      className={cn("inline-flex max-w-full items-center rounded-sm border", toneClasses[tone], tagSizes[size], className)}
      {...props}
    >
      {Icon && <Icon aria-hidden className="shrink-0" />}
      {text}
    </span>
  )
}

export { Tag }

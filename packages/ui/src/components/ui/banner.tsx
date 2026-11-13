import * as React from "react"
import type { LucideIcon } from "lucide-react"
import { cn } from "#lib/utils"
import { toneClasses, useLabel, type Labelled } from "#components/ui/chip"

export type BannerProps = Omit<React.ComponentProps<"div">, "children" | "title"> & Labelled<"title"> & {
  icon?: LucideIcon
  /** The notice's text, under the title. */
  children?: React.ReactNode
  /** One optional action, at the end. */
  action?: React.ReactNode
}

/**
 * A full-width notice (MDV-P1-083): tone, icon, title, text, one optional action. Announced: danger as an alert, the rest politely.
 * The title is required; the ai tone's title is always the AI draft words.
 */
function Banner({ tone, title, icon: Icon, action, className, children, ...props }: BannerProps) {
  const heading = useLabel(tone, title)
  return (
    <div
      data-slot="banner"
      data-tone={tone}
      role={tone === "danger" ? "alert" : "status"}
      className={cn("flex w-full flex-wrap items-start gap-x-3 gap-y-2 rounded-md border p-3 text-body", toneClasses[tone], className)}
      {...props}
    >
      {Icon && <Icon aria-hidden className="mt-0.5 size-5 shrink-0" />}
      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <p className="text-label">{heading}</p>
        {children && <div>{children}</div>}
      </div>
      {action && <div className="shrink-0">{action}</div>}
    </div>
  )
}

export { Banner }

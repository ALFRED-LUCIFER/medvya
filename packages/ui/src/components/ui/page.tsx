import * as React from "react"
import { cn } from "#lib/utils"

/** The three page widths. One place sets them, so every page lines up with the shell (MDV audit UX-01). */
export const pageWidths = {
  /** Lists, boards, schedules: use the whole workspace. */
  fluid: "max-w-none",
  /** One form or one decision: a comfortable single column. */
  form: "max-w-3xl",
  /** Record and settings pages: wide enough for two columns, narrow enough to read. */
  reading: "max-w-6xl",
} as const

export type PageWidth = keyof typeof pageWidths

function PageContainer({
  width = "fluid",
  className,
  ...props
}: React.ComponentProps<"div"> & { width?: PageWidth }) {
  return (
    <div
      data-slot="page-container"
      data-width={width}
      className={cn("flex w-full min-w-0 flex-col gap-4", pageWidths[width], className)}
      {...props}
    />
  )
}

type PageHeaderProps = Omit<React.ComponentProps<"header">, "title"> & {
  title: React.ReactNode
  /** Small label above the title, e.g. the section name. */
  eyebrow?: React.ReactNode
  description?: React.ReactNode
  /** Buttons and status chips, placed beside the title and wrapping below it on narrow screens. */
  actions?: React.ReactNode
}

/** The page's single h1, always the same size. */
function PageHeader({ title, eyebrow, description, actions, className, children, ...props }: PageHeaderProps) {
  return (
    <header
      data-slot="page-header"
      className={cn("flex flex-wrap items-end justify-between gap-3", className)}
      {...props}
    >
      <div className="flex min-w-0 flex-col gap-1">
        {eyebrow && <p className="text-[13px] font-bold text-action-600">{eyebrow}</p>}
        <h1 className="text-2xl font-extrabold tracking-tight">{title}</h1>
        {description && <p className="text-muted-foreground">{description}</p>}
        {children}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </header>
  )
}

export { PageContainer, PageHeader }
